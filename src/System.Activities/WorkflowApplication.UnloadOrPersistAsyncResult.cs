// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Hosting;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.DurableInstancing;
    using System.Collections.Generic;
    using System.Threading;
    using System.Transactions;
    using System.Xml.Linq;

    public sealed partial class WorkflowApplication
    {
        private class UnloadOrPersistAsyncResult : TransactedAsyncResult
        {
            private static readonly AsyncCompletion completeContextCallback = new AsyncCompletion(OnCompleteContext);
            private static readonly AsyncCompletion persistedCallback = new AsyncCompletion(OnPersisted);
            private static readonly AsyncCompletion savedCallback = new AsyncCompletion(OnSaved);
            private static readonly AsyncCompletion trackingCompleteCallback = new AsyncCompletion(OnTrackingComplete);
            private static Action<AsyncResult, Exception> completeCallback = new Action<AsyncResult, Exception>(OnComplete);
            private static AsyncCompletion deleteOwnerCompleteCallback = new AsyncCompletion(OnOwnerDeleted);
            private static AsyncCompletion initializedCallback = new AsyncCompletion(OnProviderInitialized);
            private static AsyncCompletion readynessEnsuredCallback = new AsyncCompletion(OnProviderReadynessEnsured);
            private static Action<object, TimeoutException> waitCompleteCallback = new Action<object, TimeoutException>(OnWaitComplete);
            private readonly bool isInternalPersist;
            private readonly bool isUnloaded;
            private WorkflowPersistenceContext context;
            private IDictionary<XName, InstanceValue> data;
            private DependentTransaction dependentTransaction;
            private WorkflowApplication instance;
            private RequiresPersistenceOperation instanceOperation;
            private PersistenceOperation operation;
            private PersistencePipeline pipeline;
            private TimeoutHelper timeoutHelper;

            public UnloadOrPersistAsyncResult(WorkflowApplication instance, TimeSpan timeout, PersistenceOperation operation,
                bool isWorkflowThread, bool isInternalPersist, AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.instance = instance;
                this.timeoutHelper = new TimeoutHelper(timeout);
                this.operation = operation;
                this.isInternalPersist = isInternalPersist;
                this.isUnloaded = (operation == PersistenceOperation.Unload || operation == PersistenceOperation.Complete);

                this.OnCompleting = UnloadOrPersistAsyncResult.completeCallback;

                bool completeSelf;
                var success = false;

                // Save off the current transaction in case we have an async operation before we end
                // up creating the WorkflowPersistenceContext and create it on another thread. Do a
                // blocking dependent clone that we will complete when we are completed.
                //
                // This will throw TransactionAbortedException by design, if the transaction is
                // already rolled back.
                var currentTransaction = Transaction.Current;
                if (currentTransaction != null)
                {
                    this.dependentTransaction = currentTransaction.DependentClone(DependentCloneOption.BlockCommitUntilComplete);
                }

                try
                {
                    if (isWorkflowThread)
                    {
                        Fx.Assert(this.instance.Controller.IsPersistable, "The runtime won't schedule this work item unless we've passed the guard");

                        // We're an internal persistence on the workflow thread which means that we
                        // are passed the guard already, we have the lock, and we know we aren't detached.

                        completeSelf = this.InitializeProvider();
                        success = true;
                    }
                    else
                    {
                        this.instanceOperation = new RequiresPersistenceOperation();
                        try
                        {
                            if (this.instance.WaitForTurnAsync(this.instanceOperation, this.timeoutHelper.RemainingTime(), waitCompleteCallback, this))
                            {
                                completeSelf = this.ValidateState();
                            }
                            else
                            {
                                completeSelf = false;
                            }
                            success = true;
                        }
                        finally
                        {
                            if (!success)
                            {
                                this.NotifyOperationComplete();
                            }
                        }
                    }
                }
                finally
                {
                    // If we had an exception, we need to complete the dependent transaction.
                    if (!success)
                    {
                        if (this.dependentTransaction != null)
                        {
                            this.dependentTransaction.Complete();
                        }
                    }
                }

                if (completeSelf)
                {
                    this.Complete(true);
                }
            }

            public static void End(IAsyncResult result) => AsyncResult.End<UnloadOrPersistAsyncResult>(result);

            private static void OnComplete(AsyncResult result, Exception exception)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result;
                try
                {
                    thisPtr.NotifyOperationComplete();
                }
                finally
                {
                    if (thisPtr.dependentTransaction != null)
                    {
                        thisPtr.dependentTransaction.Complete();
                    }
                }
            }

            private static bool OnCompleteContext(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                thisPtr.context.EndComplete(result);

                return thisPtr.DeleteOwner();
            }

            private static bool OnOwnerDeleted(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                thisPtr.instance.persistenceManager.EndDeleteOwner(result);
                return thisPtr.CloseInstance();
            }

            private static bool OnPersisted(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                var success = false;
                try
                {
                    thisPtr.instance.persistenceManager.EndSave(result);
                    success = true;
                }
                finally
                {
                    if (!success)
                    {
                        thisPtr.context.Abort();
                    }
                }
                return thisPtr.Save();
            }

            private static bool OnProviderInitialized(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                thisPtr.instance.persistenceManager.EndInitialize(result);
                return thisPtr.EnsureProviderReadyness();
            }

            private static bool OnProviderReadynessEnsured(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                thisPtr.instance.persistenceManager.EndEnsureReadyness(result);
                return thisPtr.Track();
            }

            private static bool OnSaved(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;

                var success = false;
                try
                {
                    thisPtr.pipeline.EndSave(result);
                    success = true;
                }
                finally
                {
                    thisPtr.instance.persistencePipelineInUse = null;
                    if (!success)
                    {
                        thisPtr.context.Abort();
                    }
                }

                return thisPtr.CompleteContext();
            }

            private static bool OnTrackingComplete(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                thisPtr.instance.Controller.EndFlushTrackingRecords(result);
                return thisPtr.CollectAndMap();
            }

            private static void OnWaitComplete(object state, TimeoutException asyncException)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)state;
                if (asyncException != null)
                {
                    thisPtr.Complete(false, asyncException);
                    return;
                }

                bool completeSelf;
                Exception completionException = null;

                try
                {
                    completeSelf = thisPtr.ValidateState();
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    completionException = e;
                    completeSelf = true;
                }

                if (completeSelf)
                {
                    thisPtr.Complete(false, completionException);
                }
            }

            private bool CloseInstance()
            {
                // NOTE: We need to make sure that any changes which occur here are appropriately
                // ported to WorkflowApplication's CompletionHandler.OnStage1Complete method in the
                // case where we don't call BeginPersist.
                if (this.operation != PersistenceOperation.Save)
                {
                    // Stop execution if we've given up the instance lock
                    this.instance.state = WorkflowApplicationState.Paused;
                }

                if (this.isUnloaded)
                {
                    this.instance.MarkUnloaded();
                }

                return true;
            }

            private bool CollectAndMap()
            {
                var success = false;
                try
                {
                    if (this.instance.HasPersistenceModule)
                    {
                        var modules = this.instance.GetExtensions<IPersistencePipelineModule>();
                        this.pipeline = new PersistencePipeline(modules, PersistenceManager.GenerateInitialData(this.instance));
                        this.pipeline.Collect();
                        this.pipeline.Map();
                        this.data = this.pipeline.Values;
                    }
                    success = true;
                }
                finally
                {
                    if (!success && this.context != null)
                    {
                        this.context.Abort();
                    }
                }

                if (this.instance.HasPersistenceProvider)
                {
                    return this.Persist();
                }
                else
                {
                    return this.Save();
                }
            }

            private bool CompleteContext()
            {
                var wentAsync = false;
                IAsyncResult completeResult = null;

                if (this.context != null)
                {
                    wentAsync = this.context.TryBeginComplete(this.PrepareAsyncCompletion(completeContextCallback), this, out completeResult);
                }

                if (wentAsync)
                {
                    Fx.Assert(completeResult != null, "We shouldn't have null here because we would have rethrown or gotten false for went async.");
                    return this.SyncContinue(completeResult);
                }
                else
                {
                    // We completed synchronously if we didn't get an async result out of TryBeginComplete
                    return this.DeleteOwner();
                }
            }

            private bool DeleteOwner()
            {
                if (this.instance.HasPersistenceProvider && this.instance.persistenceManager.OwnerWasCreated &&
                    (this.operation == PersistenceOperation.Unload || this.operation == PersistenceOperation.Complete))
                {
                    // This call uses the ambient transaction directly if there was one, to mimic
                    // the sync case. TODO, 124600, suppress the transaction always.
                    IAsyncResult deleteOwnerResult = null;
                    using (this.PrepareTransactionalCall(this.dependentTransaction))
                    {
                        deleteOwnerResult = this.instance.persistenceManager.BeginDeleteOwner(this.timeoutHelper.RemainingTime(),
                            this.PrepareAsyncCompletion(UnloadOrPersistAsyncResult.deleteOwnerCompleteCallback), this);
                    }
                    return this.SyncContinue(deleteOwnerResult);
                }
                else
                {
                    return this.CloseInstance();
                }
            }

            private bool EnsureProviderReadyness()
            {
                if (this.instance.HasPersistenceProvider && !this.instance.persistenceManager.IsLocked && this.dependentTransaction != null)
                {
                    var result = this.instance.persistenceManager.BeginEnsureReadyness(this.timeoutHelper.RemainingTime(),
                        this.PrepareAsyncCompletion(UnloadOrPersistAsyncResult.readynessEnsuredCallback), this);
                    return this.SyncContinue(result);
                }
                else
                {
                    return this.Track();
                }
            }

            private bool InitializeProvider()
            {
                // We finally have the lock and are passed the guard. Let's update our operation if
                // this is an Unload.
                if (this.operation == PersistenceOperation.Unload && this.instance.Controller.State == WorkflowInstanceState.Complete)
                {
                    this.operation = PersistenceOperation.Complete;
                }

                if (this.instance.HasPersistenceProvider && !this.instance.persistenceManager.IsInitialized)
                {
                    var result = this.instance.persistenceManager.BeginInitialize(this.instance.DefinitionIdentity, this.timeoutHelper.RemainingTime(),
                        this.PrepareAsyncCompletion(UnloadOrPersistAsyncResult.initializedCallback), this);
                    return this.SyncContinue(result);
                }
                else
                {
                    return this.EnsureProviderReadyness();
                }
            }

            private void NotifyOperationComplete()
            {
                var localInstanceOperation = this.instanceOperation;
                this.instanceOperation = null;
                this.instance.NotifyOperationComplete(localInstanceOperation);
            }

            private bool Persist()
            {
                IAsyncResult result = null;
                try
                {
                    if (this.data == null)
                    {
                        this.data = PersistenceManager.GenerateInitialData(this.instance);
                    }

                    if (this.context == null)
                    {
                        this.context = new WorkflowPersistenceContext(this.pipeline != null && this.pipeline.IsSaveTransactionRequired,
                            this.dependentTransaction, this.timeoutHelper.OriginalTimeout);
                    }

                    using (this.PrepareTransactionalCall(this.context.PublicTransaction))
                    {
                        result = this.instance.persistenceManager.BeginSave(this.data, this.operation, this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(persistedCallback), this);
                    }
                }
                finally
                {
                    if (result == null && this.context != null)
                    {
                        this.context.Abort();
                    }
                }
                return this.SyncContinue(result);
            }

            private bool Save()
            {
                if (this.pipeline != null)
                {
                    IAsyncResult result = null;
                    try
                    {
                        if (this.context == null)
                        {
                            this.context = new WorkflowPersistenceContext(this.pipeline.IsSaveTransactionRequired,
                                this.dependentTransaction, this.timeoutHelper.RemainingTime());
                        }

                        this.instance.persistencePipelineInUse = this.pipeline;
                        Thread.MemoryBarrier();
                        if (this.instance.state == WorkflowApplicationState.Aborted)
                        {
                            throw FxTrace.Exception.AsError(new OperationCanceledException(SR.DefaultAbortReason));
                        }

                        using (this.PrepareTransactionalCall(this.context.PublicTransaction))
                        {
                            result = this.pipeline.BeginSave(this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(savedCallback), this);
                        }
                    }
                    finally
                    {
                        if (result == null)
                        {
                            this.instance.persistencePipelineInUse = null;
                            if (this.context != null)
                            {
                                this.context.Abort();
                            }
                        }
                    }
                    return this.SyncContinue(result);
                }
                else
                {
                    return this.CompleteContext();
                }
            }

            private bool Track()
            {
                // Do the tracking before preparing in case the tracking data is being pushed into
                // an extension and persisted transactionally with the instance state.

                if (this.instance.HasPersistenceProvider)
                {
                    // We only track the persistence operation if we actually are persisting (and
                    // not just hitting PersistenceParticipants)
                    this.instance.TrackPersistence(this.operation);
                }

                if (this.instance.Controller.HasPendingTrackingRecords)
                {
                    TimeSpan flushTrackingRecordsTimeout;

                    if (this.isInternalPersist)
                    {
                        // If we're an internal persist we're using TimeSpan.MaxValue for our
                        // persistence and we want to use a smaller timeout for tracking
                        flushTrackingRecordsTimeout = ActivityDefaults.TrackingTimeout;
                    }
                    else
                    {
                        flushTrackingRecordsTimeout = this.timeoutHelper.RemainingTime();
                    }

                    var result = this.instance.Controller.BeginFlushTrackingRecords(flushTrackingRecordsTimeout, this.PrepareAsyncCompletion(trackingCompleteCallback), this);
                    return this.SyncContinue(result);
                }

                return this.CollectAndMap();
            }

            private bool ValidateState()
            {
                var alreadyUnloaded = false;
                if (this.operation == PersistenceOperation.Unload)
                {
                    this.instance.ValidateStateForUnload();
                    alreadyUnloaded = this.instance.state == WorkflowApplicationState.Unloaded;
                }
                else
                {
                    this.instance.ValidateStateForPersist();
                }

                if (alreadyUnloaded)
                {
                    return true;
                }
                else
                {
                    return this.InitializeProvider();
                }
            }
        }
    }
}