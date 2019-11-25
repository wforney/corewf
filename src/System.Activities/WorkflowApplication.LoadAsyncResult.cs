// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.DynamicUpdate;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.DurableInstancing;
    using System.Collections.Generic;
    using System.Threading;
    using System.Transactions;
    using System.Xml.Linq;

    public sealed partial class WorkflowApplication
    {
        private class LoadAsyncResult : TransactedAsyncResult
        {
            private static readonly AsyncCompletion completeContextCallback = new AsyncCompletion(OnCompleteContext);
            private static readonly AsyncCompletion loadCompleteCallback = new AsyncCompletion(OnLoadComplete);
            private static readonly AsyncCompletion loadPipelineCallback = new AsyncCompletion(OnLoadPipeline);
            private static readonly AsyncCompletion providerRegisteredCallback = new AsyncCompletion(OnProviderRegistered);
            private static readonly Action<object, TimeoutException> waitCompleteCallback = new Action<object, TimeoutException>(OnWaitComplete);
            private static Action<AsyncResult, Exception> completeCallback = new Action<AsyncResult, Exception>(OnComplete);
            private readonly WorkflowApplication application;
            private readonly bool loadAny;
            private readonly PersistenceManager persistenceManager;
            private readonly TimeoutHelper timeoutHelper;
            private WorkflowPersistenceContext context;
            private DependentTransaction dependentTransaction;
            private object deserializedRuntimeState;
            private InstanceOperation instanceOperation;
            private PersistencePipeline pipeline;
            private DynamicUpdateMap updateMap;
            private IDictionary<XName, InstanceValue> values;

            public LoadAsyncResult(WorkflowApplication application, PersistenceManager persistenceManager,
                IDictionary<XName, InstanceValue> values,

                        DynamicUpdateMap updateMap,

                TimeSpan timeout,
                AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.application = application;
                this.persistenceManager = persistenceManager;
                this.values = values;
                this.timeoutHelper = new TimeoutHelper(timeout);

                this.updateMap = updateMap;

                this.Initialize();
            }

            public LoadAsyncResult(WorkflowApplication application, PersistenceManager persistenceManager,
                bool loadAny, TimeSpan timeout, AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.application = application;
                this.persistenceManager = persistenceManager;
                this.loadAny = loadAny;
                this.timeoutHelper = new TimeoutHelper(timeout);

                this.Initialize();
            }

            public static void End(IAsyncResult result) => AsyncResult.End<LoadAsyncResult>(result);

            public static WorkflowApplicationInstance EndAndCreateInstance(IAsyncResult result)
            {
                var thisPtr = AsyncResult.End<LoadAsyncResult>(result);
                Fx.AssertAndThrow(thisPtr.application == null, "Should not create a WorkflowApplicationInstance if we already have a WorkflowApplication");

                var deserializedRuntimeState = WorkflowApplication.ExtractRuntimeState(thisPtr.values, thisPtr.persistenceManager.InstanceId);
                return new WorkflowApplicationInstance(thisPtr.persistenceManager, thisPtr.values, deserializedRuntimeState.WorkflowIdentity);
            }

            private static void Abort(LoadAsyncResult thisPtr, Exception exception)
            {
                if (thisPtr.application == null)
                {
                    thisPtr.persistenceManager.Abort();
                }
                else
                {
                    thisPtr.application.AbortDueToException(exception);
                }
            }

            private static void OnComplete(AsyncResult result, Exception exception)
            {
                var thisPtr = (LoadAsyncResult)result;
                try
                {
                    if (thisPtr.dependentTransaction != null)
                    {
                        thisPtr.dependentTransaction.Complete();
                    }

                    if (exception != null)
                    {
                        Abort(thisPtr, exception);
                    }
                }
                finally
                {
                    thisPtr.NotifyOperationComplete();
                }
            }

            private static bool OnCompleteContext(IAsyncResult result)
            {
                var thisPtr = (LoadAsyncResult)result.AsyncState;
                thisPtr.context.EndComplete(result);
                return thisPtr.Finish();
            }

            private static bool OnLoadComplete(IAsyncResult result)
            {
                var thisPtr = (LoadAsyncResult)result.AsyncState;
                return thisPtr.LoadValues(result);
            }

            private static bool OnLoadPipeline(IAsyncResult result)
            {
                var thisPtr = (LoadAsyncResult)result.AsyncState;

                var success = false;
                try
                {
                    thisPtr.pipeline.EndLoad(result);
                    success = true;
                }
                finally
                {
                    if (!success)
                    {
                        thisPtr.context.Abort();
                    }
                }
                return thisPtr.CompleteContext();
            }

            private static bool OnProviderRegistered(IAsyncResult result)
            {
                var thisPtr = (LoadAsyncResult)result.AsyncState;
                thisPtr.persistenceManager.EndInitialize(result);
                return thisPtr.Load();
            }

            private static void OnWaitComplete(object state, TimeoutException asyncException)
            {
                var thisPtr = (LoadAsyncResult)state;
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

            private bool CompleteContext()
            {
                if (this.application != null)
                {
                    this.application.Initialize(this.deserializedRuntimeState, this.updateMap);
                    if (this.updateMap != null)
                    {
                        this.application.UpdateInstanceMetadata();
                    }
                }

                if (this.context.TryBeginComplete(this.PrepareAsyncCompletion(completeContextCallback), this, out var completeResult))
                {
                    Fx.Assert(completeResult != null, "We shouldn't have null here.");
                    return this.SyncContinue(completeResult);
                }
                else
                {
                    return this.Finish();
                }
            }

            private bool Finish()
            {
                if (this.pipeline != null)
                {
                    this.pipeline.Publish();
                }
                return true;
            }

            private void Initialize()
            {
                this.OnCompleting = LoadAsyncResult.completeCallback;

                // Save off the current transaction in case we have an async operation before we end
                // up creating the WorkflowPersistenceContext and create it on another thread. Do a
                // simple clone here to prevent the object referenced by Transaction.Current from
                // disposing before we get around to referencing it when we create the WorkflowPersistenceContext.
                //
                // This will throw TransactionAbortedException by design, if the transaction is
                // already rolled back.
                var currentTransaction = Transaction.Current;
                if (currentTransaction != null)
                {
                    this.dependentTransaction = currentTransaction.DependentClone(DependentCloneOption.BlockCommitUntilComplete);
                }

                bool completeSelf;
                var success = false;
                Exception updateException = null;
                try
                {
                    if (this.application == null)
                    {
                        completeSelf = this.RegisterProvider();
                    }
                    else
                    {
                        completeSelf = this.WaitForTurn();
                    }
                    success = true;
                }
                catch (InstanceUpdateException e)
                {
                    updateException = e;
                    throw;
                }
                catch (VersionMismatchException e)
                {
                    updateException = e;
                    throw;
                }
                finally
                {
                    if (!success)
                    {
                        if (this.dependentTransaction != null)
                        {
                            this.dependentTransaction.Complete();
                        }
                        Abort(this, updateException);
                    }
                }

                if (completeSelf)
                {
                    this.Complete(true);
                }
            }

            private bool Load()
            {
                var success = false;
                IAsyncResult result = null;
                try
                {
                    var transactionRequired = this.application != null ? this.application.IsLoadTransactionRequired() : false;
                    this.context = new WorkflowPersistenceContext(transactionRequired,
                        this.dependentTransaction, this.timeoutHelper.OriginalTimeout);

                    // Values is null if this is an initial load from the database. It is non-null
                    // if we already loaded values into a WorkflowApplicationInstance, and are now
                    // loading from that WAI.
                    if (this.values == null)
                    {
                        using (this.PrepareTransactionalCall(this.context.PublicTransaction))
                        {
                            if (this.loadAny)
                            {
                                result = this.persistenceManager.BeginTryLoad(this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(loadCompleteCallback), this);
                            }
                            else
                            {
                                result = this.persistenceManager.BeginLoad(this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(loadCompleteCallback), this);
                            }
                        }
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

                if (result == null)
                {
                    return this.LoadValues(null);
                }
                else
                {
                    return this.SyncContinue(result);
                }
            }

            private bool LoadValues(IAsyncResult result)
            {
                IAsyncResult loadResult = null;
                var success = false;
                try
                {
                    Fx.Assert((result == null) != (this.values == null), "We should either have values already retrieved, or an IAsyncResult to retrieve them");

                    if (result != null)
                    {
                        if (this.loadAny)
                        {
                            if (!this.persistenceManager.EndTryLoad(result, out this.values))
                            {
                                throw FxTrace.Exception.AsError(new InstanceNotReadyException(SR.NoRunnableInstances));
                            }
                            if (this.application != null)
                            {
                                if (this.application.instanceIdSet)
                                {
                                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
                                }

                                this.application.instanceId = this.persistenceManager.InstanceId;
                                this.application.instanceIdSet = true;
                            }
                        }
                        else
                        {
                            this.values = this.persistenceManager.EndLoad(result);
                        }
                    }

                    if (this.application != null)
                    {
                        this.pipeline = this.application.ProcessInstanceValues(this.values, out this.deserializedRuntimeState);

                        if (this.pipeline != null)
                        {
                            this.pipeline.SetLoadedValues(this.values);

                            this.application.persistencePipelineInUse = this.pipeline;
                            Thread.MemoryBarrier();
                            if (this.application.state == WorkflowApplicationState.Aborted)
                            {
                                throw FxTrace.Exception.AsError(new OperationCanceledException(SR.DefaultAbortReason));
                            }

                            using (this.PrepareTransactionalCall(this.context.PublicTransaction))
                            {
                                loadResult = this.pipeline.BeginLoad(this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(loadPipelineCallback), this);
                            }
                        }
                    }

                    success = true;
                }
                finally
                {
                    if (!success)
                    {
                        this.context.Abort();
                    }
                }

                if (this.pipeline != null)
                {
                    return this.SyncContinue(loadResult);
                }
                else
                {
                    return this.CompleteContext();
                }
            }

            private void NotifyOperationComplete()
            {
                if (this.application != null)
                {
                    var localInstanceOperation = this.instanceOperation;
                    this.instanceOperation = null;
                    this.application.NotifyOperationComplete(localInstanceOperation);
                }
            }

            private bool RegisterProvider()
            {
                if (!this.persistenceManager.IsInitialized)
                {
                    var definitionIdentity = this.application != null ? this.application.DefinitionIdentity : WorkflowApplication.unknownIdentity;
                    var result = this.persistenceManager.BeginInitialize(definitionIdentity, this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(providerRegisteredCallback), this);
                    return this.SyncContinue(result);
                }
                else
                {
                    return this.Load();
                }
            }

            private bool ValidateState()
            {
                this.application.ValidateStateForLoad();

                this.application.SetPersistenceManager(this.persistenceManager);
                if (!this.loadAny)
                {
                    this.application.instanceId = this.persistenceManager.InstanceId;
                    this.application.instanceIdSet = true;
                }
                if (this.application.InstanceStore == null)
                {
                    this.application.InstanceStore = this.persistenceManager.InstanceStore;
                }

                return this.RegisterProvider();
            }

            private bool WaitForTurn()
            {
                bool completeSelf;
                var success = false;
                this.instanceOperation = new InstanceOperation { RequiresInitialized = false };
                try
                {
                    if (this.application.WaitForTurnAsync(this.instanceOperation, this.timeoutHelper.RemainingTime(), waitCompleteCallback, this))
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

                return completeSelf;
            }
        }
    }
}