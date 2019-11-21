// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.DurableInstancing;
    using System.Activities.Hosting;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.DurableInstancing;
    using System.Activities.Tracking;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading;
    using System.Transactions;
    using System.Xml.Linq;

    /// <summary>
    /// The WorkflowApplication class. This class cannot be inherited. Implements the <see
    /// cref="System.Activities.Hosting.WorkflowInstance" />
    /// </summary>
    /// <seealso cref="System.Activities.Hosting.WorkflowInstance" />
    /// <remarks>
    /// WorkflowApplication is free-threaded. It is responsible for the correct locking and usage of
    /// the ActivityExecutor. Given that there are two simultaneous users of ActivityExecutor
    /// (WorkflowApplication and NativeActivityContext), it is imperative that WorkflowApplication
    /// only calls into ActivityExecutor when there are no activities executing (and thus no worries
    /// about colliding with AEC calls). SYNCHRONIZATION SCHEME WorkflowInstance is defined to not
    /// be thread safe and to disallow all operations while it is (potentially
    /// asynchronously) running. The WorkflowInstance is in the "running" state between a call to Run
    /// and the subsequent call to either WorkflowInstance NotifyPaused or NotifyUnhandledException.
    /// WorkflowApplication keeps track of a boolean "isBusy" and a list of pending operations. WI
    /// is busy whenever it is servicing an operation or the runtime is in the "running" state.
    /// Enqueue - This enqueues an operation into the pending operation list. If WI is not busy then
    /// the operation can be serviced immediately. This is the only place where "isBusy" flips to
    /// true. OnNotifiedUnhandledException - This method performs some processing and then calls
    /// OnNotifiedPaused. OnNotifiedPaused - This method is only ever called when "isBusy" is true.
    /// It first checks to see if there is other work to be done (prioritization: raise completed,
    /// handle an operation, resume execution, raise idle, stop). This is the only place where
    /// "isBusy" flips to false and this only occurs when there is no other work to be done.
    /// [Force]NotifyOperationComplete - These methods are called by individual operations when they
    /// are done processing. If the operation was notified (IE - actually performed in the eyes of
    /// WI) then this is simply a call to OnNotifiedPaused. Operation notification - The
    /// InstanceOperation class keeps tracks of whether a specified operation was dispatched by WI
    /// or not. If it was dispatched (determined either in Enqueue, FindOperation, or Remove) then
    /// it MUST result in a call to OnNotifiedPaused when complete.
    /// </remarks>
    [Fx.Tag.XamlVisible(false)]
    public sealed class WorkflowApplication : WorkflowInstance
    {
        private static AsyncCallback eventFrameCallback;
        private static IdleEventHandler idleHandler;
        private static CompletedEventHandler completedHandler;
        private static UnhandledExceptionEventHandler unhandledExceptionHandler;
        private static Action<object, TimeoutException> waitAsyncCompleteCallback;
        private static readonly WorkflowIdentity unknownIdentity = new WorkflowIdentity();
        private Action<WorkflowApplicationAbortedEventArgs> onAborted;
        private Action<WorkflowApplicationEventArgs> onUnloaded;
        private Action<WorkflowApplicationCompletedEventArgs> onCompleted;
        private Func<WorkflowApplicationUnhandledExceptionEventArgs, UnhandledExceptionAction> onUnhandledException;
        private Func<WorkflowApplicationIdleEventArgs, PersistableIdleAction> onPersistableIdle;
        private Action<WorkflowApplicationIdleEventArgs> onIdle;
        private WorkflowEventData eventData;
        private WorkflowInstanceExtensionManager extensions;
        private PersistencePipeline persistencePipelineInUse;
        private InstanceStore instanceStore;
        private PersistenceManager persistenceManager;
        private WorkflowApplicationState state;
        private int handlerThreadId;
        private bool isInHandler;
        private Action invokeCompletedCallback;
        private Guid instanceId;
        private bool instanceIdSet;  // Checking for Guid.Empty is expensive.

        // Tracking for one-time actions per in-memory pulse
        private bool hasCalledAbort;

        private bool hasCalledRun;

        // Tracking for one-time actions per instance lifetime (these end up being persisted)
        private bool hasRaisedCompleted;

        private readonly Quack<InstanceOperation> pendingOperations;
        private bool isBusy;
        private bool hasExecutionOccurredSinceLastIdle;

        // Count of operations that are about to be enqueued. We use this when enqueueing multiple
        // operations, to avoid raising idle on dequeue of the first operation.
        private int pendingUnenqueued;

        // We use this to keep track of the number of "interesting" things that have happened.
        // Notifying operations and calling Run on the runtime count as interesting things. All
        // operations are stamped with the actionCount at the time of being enqueued.
        private int actionCount;

        // Initial creation data
        private readonly IDictionary<string, object> initialWorkflowArguments;

        private readonly IList<Handle> rootExecutionProperties;
        private IDictionary<XName, InstanceValue> instanceMetadata;

        public WorkflowApplication(Activity workflowDefinition)
            : this(workflowDefinition, (WorkflowIdentity)null)
        {
        }

        public WorkflowApplication(Activity workflowDefinition, IDictionary<string, object> inputs)
            : this(workflowDefinition, inputs, (WorkflowIdentity)null)
        {
        }

        public WorkflowApplication(Activity workflowDefinition, WorkflowIdentity definitionIdentity)
            : base(workflowDefinition, definitionIdentity)
        {
            this.pendingOperations = new Quack<InstanceOperation>();
            Fx.Assert(this.state == WorkflowApplicationState.Paused, "We always start out paused (the default)");
        }

        public WorkflowApplication(Activity workflowDefinition, IDictionary<string, object> inputs, WorkflowIdentity definitionIdentity)
            : this(workflowDefinition, definitionIdentity) => this.initialWorkflowArguments = inputs ?? throw FxTrace.Exception.ArgumentNull(nameof(inputs));

        private WorkflowApplication(Activity workflowDefinition, IDictionary<string, object> inputs, IList<Handle> executionProperties)
            : this(workflowDefinition)
        {
            this.initialWorkflowArguments = inputs;
            this.rootExecutionProperties = executionProperties;
        }

        public InstanceStore InstanceStore
        {
            get => this.instanceStore;
            set
            {
                this.ThrowIfReadOnly();
                this.instanceStore = value;
            }
        }

        public WorkflowInstanceExtensionManager Extensions
        {
            get
            {
                if (this.extensions == null)
                {
                    this.extensions = new WorkflowInstanceExtensionManager();
                    if (base.IsReadOnly)
                    {
                        this.extensions.MakeReadOnly();
                    }
                }

                return this.extensions;
            }
        }

        public Action<WorkflowApplicationAbortedEventArgs> Aborted
        {
            get => this.onAborted;
            set
            {
                this.ThrowIfMulticast(value);
                this.onAborted = value;
            }
        }

        public Action<WorkflowApplicationEventArgs> Unloaded
        {
            get => this.onUnloaded;
            set
            {
                this.ThrowIfMulticast(value);
                this.onUnloaded = value;
            }
        }

        public Action<WorkflowApplicationCompletedEventArgs> Completed
        {
            get => this.onCompleted;
            set
            {
                this.ThrowIfMulticast(value);
                this.onCompleted = value;
            }
        }

        public Func<WorkflowApplicationUnhandledExceptionEventArgs, UnhandledExceptionAction> OnUnhandledException
        {
            get => this.onUnhandledException;
            set
            {
                this.ThrowIfMulticast(value);
                this.onUnhandledException = value;
            }
        }

        public Action<WorkflowApplicationIdleEventArgs> Idle
        {
            get => this.onIdle;
            set
            {
                this.ThrowIfMulticast(value);
                this.onIdle = value;
            }
        }

        public Func<WorkflowApplicationIdleEventArgs, PersistableIdleAction> PersistableIdle
        {
            get => this.onPersistableIdle;
            set
            {
                this.ThrowIfMulticast(value);
                this.onPersistableIdle = value;
            }
        }

        public override Guid Id
        {
            get
            {
                if (!this.instanceIdSet)
                {
                    lock (this.pendingOperations)
                    {
                        if (!this.instanceIdSet)
                        {
                            this.instanceId = Guid.NewGuid();
                            this.instanceIdSet = true;
                        }
                    }
                }

                return this.instanceId;
            }
        }

        protected internal override bool SupportsInstanceKeys => false;

        private static AsyncCallback EventFrameCallback
        {
            get
            {
                if (eventFrameCallback == null)
                {
                    eventFrameCallback = Fx.ThunkCallback(new AsyncCallback(EventFrame));
                }

                return eventFrameCallback;
            }
        }

        private WorkflowEventData EventData
        {
            get
            {
                if (this.eventData == null)
                {
                    this.eventData = new WorkflowEventData(this);
                }

                return this.eventData;
            }
        }

        private bool HasPersistenceProvider => this.persistenceManager != null;

        private bool IsHandlerThread => this.isInHandler && this.handlerThreadId == Thread.CurrentThread.ManagedThreadId;

        private bool IsInTerminalState => this.state == WorkflowApplicationState.Unloaded || this.state == WorkflowApplicationState.Aborted;

        public void AddInitialInstanceValues(IDictionary<XName, object> writeOnlyValues)
        {
            this.ThrowIfReadOnly();

            if (writeOnlyValues != null)
            {
                if (this.instanceMetadata == null)
                {
                    this.instanceMetadata = new Dictionary<XName, InstanceValue>(writeOnlyValues.Count);
                }

                foreach (var pair in writeOnlyValues)
                {
                    // We use the indexer so that we can replace keys that already exist
                    this.instanceMetadata[pair.Key] = new InstanceValue(pair.Value, InstanceValueOptions.Optional | InstanceValueOptions.WriteOnly);
                }
            }
        }

        // host-facing access to our cascading ExtensionManager resolution. Used by WorkflowApplicationEventArgs
        internal IEnumerable<T> InternalGetExtensions<T>() where T : class => base.GetExtensions<T>();

        private static void EventFrame(IAsyncResult result)
        {
            if (result.CompletedSynchronously)
            {
                return;
            }

            var data = (WorkflowEventData)result.AsyncState;
            var thisPtr = data.Instance;

            var done = true;

            try
            {
                Exception abortException = null;

                try
                {
                    // The "false" is to notify that we are not still sync
                    done = data.NextCallback(result, thisPtr, false);
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    abortException = e;
                }

                if (abortException != null)
                {
                    thisPtr.AbortInstance(abortException, true);
                }
            }
            finally
            {
                if (done)
                {
                    thisPtr.OnNotifyPaused();
                }
            }
        }

        private bool ShouldRaiseComplete(WorkflowInstanceState state) => state == WorkflowInstanceState.Complete && !this.hasRaisedCompleted;

        private void Enqueue(InstanceOperation operation) => this.Enqueue(operation, false);

        private void Enqueue(InstanceOperation operation, bool push)
        {
            lock (this.pendingOperations)
            {
                operation.ActionId = this.actionCount;

                if (this.isBusy)
                {
                    // If base.IsReadOnly == false, we can't call the Controller yet because
                    // WorkflowInstance is not initialized. But that's okay; if the instance isn't
                    // initialized then the scheduler's not running yet, so no need to pause it.
                    if (operation.InterruptsScheduler && base.IsReadOnly)
                    {
                        this.Controller.RequestPause();
                    }

                    this.AddToPending(operation, push);
                }
                else
                {
                    // first make sure we're ready to run
                    if (operation.RequiresInitialized)
                    {
                        this.EnsureInitialized();
                    }

                    if (!operation.CanRun(this) && !this.IsInTerminalState)
                    {
                        this.AddToPending(operation, push);
                    }
                    else
                    {
                        // Action: Notifying an operation
                        this.actionCount++;

                        // We've essentially just notified this operation that it is free to do its thing
                        try
                        {
                        }
                        finally
                        {
                            operation.Notified = true;
                            this.isBusy = true;
                        }
                    }
                }
            }
        }

        private void IncrementPendingUnenqueud()
        {
            lock (this.pendingOperations)
            {
                this.pendingUnenqueued++;
            }
        }

        private void DecrementPendingUnenqueud()
        {
            lock (this.pendingOperations)
            {
                this.pendingUnenqueued--;
            }
        }

        private void AddToPending(InstanceOperation operation, bool push)
        {
            if (base.IsReadOnly)
            {
                // We're already initialized
                operation.RequiresInitialized = false;
            }

            if (push)
            {
                this.pendingOperations.PushFront(operation);
            }
            else
            {
                this.pendingOperations.Enqueue(operation);
            }

            operation.OnEnqueued();
        }

        private bool Remove(InstanceOperation operation)
        {
            lock (this.pendingOperations)
            {
                return this.pendingOperations.Remove(operation);
            }
        }

        private bool WaitForTurn(InstanceOperation operation, TimeSpan timeout)
        {
            this.Enqueue(operation);
            return this.WaitForTurnNoEnqueue(operation, timeout);
        }

        private bool WaitForTurnNoEnqueue(InstanceOperation operation, TimeSpan timeout)
        {
            if (!operation.WaitForTurn(timeout))
            {
                if (this.Remove(operation))
                {
                    throw FxTrace.Exception.AsError(new TimeoutException(SR.TimeoutOnOperation(timeout)));
                }
            }
            return true;
        }

        private bool WaitForTurnAsync(InstanceOperation operation, TimeSpan timeout, Action<object, TimeoutException> callback, object state) => this.WaitForTurnAsync(operation, false, timeout, callback, state);

        private bool WaitForTurnAsync(InstanceOperation operation, bool push, TimeSpan timeout, Action<object, TimeoutException> callback, object state)
        {
            this.Enqueue(operation, push);

            return this.WaitForTurnNoEnqueueAsync(operation, timeout, callback, state);
        }

        private bool WaitForTurnNoEnqueueAsync(InstanceOperation operation, TimeSpan timeout, Action<object, TimeoutException> callback, object state)
        {
            if (waitAsyncCompleteCallback == null)
            {
                waitAsyncCompleteCallback = new Action<object, TimeoutException>(OnWaitAsyncComplete);
            }
            return operation.WaitForTurnAsync(timeout, waitAsyncCompleteCallback, new WaitForTurnData(callback, state, operation, this));
        }

        private static void OnWaitAsyncComplete(object state, TimeoutException exception)
        {
            var data = (WaitForTurnData)state;

            if (!data.Instance.Remove(data.Operation))
            {
                exception = null;
            }

            data.Callback(data.State, exception);
        }

        // For when we know that the operation is non-null and notified (like in async paths)
        private void ForceNotifyOperationComplete() => this.OnNotifyPaused();

        private void NotifyOperationComplete(InstanceOperation operation)
        {
            if (operation != null && operation.Notified)
            {
                this.OnNotifyPaused();
            }
        }

        private InstanceOperation FindOperation()
        {
            if (this.pendingOperations.Count > 0)
            {
                // Special case the first one
                var temp = this.pendingOperations[0];

                if (temp.RequiresInitialized)
                {
                    this.EnsureInitialized();
                }

                // Even if we can't run this operation we want to notify it if all the operations
                // are invalid. This will cause the Validate* method to throw to the caller.
                if (temp.CanRun(this) || this.IsInTerminalState)
                {
                    // Action: Notifying an operation
                    this.actionCount++;

                    temp.Notified = true;
                    this.pendingOperations.Dequeue();
                    return temp;
                }
                else
                {
                    for (var i = 0; i < this.pendingOperations.Count; i++)
                    {
                        temp = this.pendingOperations[i];

                        if (temp.RequiresInitialized)
                        {
                            this.EnsureInitialized();
                        }

                        if (temp.CanRun(this))
                        {
                            // Action: Notifying an operation
                            this.actionCount++;

                            temp.Notified = true;
                            this.pendingOperations.Remove(i);
                            return temp;
                        }
                    }
                }
            }

            return null;
        }

        // assumes that we're called under the pendingOperations lock
        private void EnsureInitialized()
        {
            if (!base.IsReadOnly)
            {
                // For newly created workflows (e.g. not the Load() case), we need to initialize now
                base.RegisterExtensionManager(this.extensions);
                base.Initialize(this.initialWorkflowArguments, this.rootExecutionProperties);

                // make sure we have a persistence manager if necessary
                if (this.persistenceManager == null && this.instanceStore != null)
                {
                    Fx.Assert(this.Id != Guid.Empty, "should have a valid Id at this point");
                    this.persistenceManager = new PersistenceManager(this.instanceStore, this.GetInstanceMetadata(), this.Id);
                }
            }
        }

        protected override void OnNotifyPaused()
        {
            Fx.Assert(this.isBusy, "We're always busy when we get this notification.");

            WorkflowInstanceState? localInstanceState = null;
            if (base.IsReadOnly)
            {
                localInstanceState = this.Controller.State;
            }
            var localApplicationState = this.state;

            var stillSync = true;

            while (stillSync)
            {
                if (localInstanceState.HasValue && this.ShouldRaiseComplete(localInstanceState.Value))
                {
                    Exception abortException = null;

                    try
                    {
                        // We're about to notify the world that this instance is completed so let's
                        // make it official.
                        this.hasRaisedCompleted = true;

                        if (completedHandler == null)
                        {
                            completedHandler = new CompletedEventHandler();
                        }
                        stillSync = completedHandler.Run(this);
                    }
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                        {
                            throw;
                        }

                        abortException = e;
                    }

                    if (abortException != null)
                    {
                        this.AbortInstance(abortException, true);
                    }
                }
                else
                {
                    InstanceOperation toRun = null;
                    bool shouldRunNow;
                    bool shouldRaiseIdleNow;

                    lock (this.pendingOperations)
                    {
                        toRun = this.FindOperation();

                        // Cache the state in local variables to ensure that none of the decision
                        // points in the ensuing "if" statement flip when control gets out of the lock.
                        shouldRunNow = (localInstanceState.HasValue && localInstanceState == WorkflowInstanceState.Runnable && localApplicationState == WorkflowApplicationState.Runnable);
                        shouldRaiseIdleNow = this.hasExecutionOccurredSinceLastIdle &&
                            localInstanceState.HasValue && localInstanceState == WorkflowInstanceState.Idle &&
                            !this.hasRaisedCompleted && this.pendingUnenqueued == 0;

                        if (toRun == null && !shouldRunNow && !shouldRaiseIdleNow)
                        {
                            this.isBusy = false;
                            stillSync = false;
                        }
                    }

                    if (toRun != null)
                    {
                        toRun.NotifyTurn();
                        stillSync = false;
                    }
                    else if (shouldRaiseIdleNow)
                    {
                        this.hasExecutionOccurredSinceLastIdle = false;

                        Fx.Assert(this.isBusy, "we must be busy if we're raising idle");

                        Exception abortException = null;

                        try
                        {
                            if (idleHandler == null)
                            {
                                idleHandler = new IdleEventHandler();
                            }
                            stillSync = idleHandler.Run(this);
                        }
                        catch (Exception e)
                        {
                            if (Fx.IsFatal(e))
                            {
                                throw;
                            }

                            abortException = e;
                        }

                        if (abortException != null)
                        {
                            this.AbortInstance(abortException, true);
                        }
                    }
                    else if (shouldRunNow)
                    {
                        this.hasExecutionOccurredSinceLastIdle = true;

                        // Action: Running the scheduler
                        this.actionCount++;

                        this.Controller.Run();
                        stillSync = false;
                    }
                }
            }
        }

        // used by WorkflowInvoker in the InvokeAsync case
        internal void GetCompletionStatus(out Exception terminationException, out bool cancelled)
        {
            var completionState = this.Controller.GetCompletionState(out var dummyOutputs, out terminationException);
            Fx.Assert(completionState != ActivityInstanceState.Executing, "Activity cannot be executing when this method is called");
            cancelled = (completionState == ActivityInstanceState.Canceled);
        }

        protected internal override void OnRequestAbort(Exception reason) => this.AbortInstance(reason, false);

        public void Abort() => this.Abort(SR.DefaultAbortReason);

        public void Abort(string reason) => this.Abort(reason, null);

        private void Abort(string reason, Exception innerException)
        {
            // This is pretty loose check, but it is okay if we go down the abort path multiple times
            if (this.state != WorkflowApplicationState.Aborted)
            {
                this.AbortInstance(new WorkflowApplicationAbortedException(reason, innerException), false);
            }
        }

        private void AbortPersistence()
        {
            if (this.persistenceManager != null)
            {
                this.persistenceManager.Abort();
            }

            var currentPersistencePipeline = this.persistencePipelineInUse;
            if (currentPersistencePipeline != null)
            {
                currentPersistencePipeline.Abort();
            }
        }

        private void AbortInstance(Exception reason, bool isWorkflowThread)
        {
            this.state = WorkflowApplicationState.Aborted;

            // Need to ensure that either components see the Aborted state, this method sees the
            // components, or both.
            Thread.MemoryBarrier();

            // We do this outside of the lock since persistence might currently be blocking access
            // to the lock.
            this.AbortPersistence();

            if (isWorkflowThread)
            {
                if (!this.hasCalledAbort)
                {
                    this.hasCalledAbort = true;
                    this.Controller.Abort(reason);

                    // We should get off this thread because we're unsure of its state
                    this.ScheduleTrackAndRaiseAborted(reason);
                }
            }
            else
            {
                var completeSelf = true;
                InstanceOperation operation = null;

                try
                {
                    operation = new InstanceOperation();

                    completeSelf = this.WaitForTurnAsync(operation, true, ActivityDefaults.AcquireLockTimeout, new Action<object, TimeoutException>(this.OnAbortWaitComplete), reason);

                    if (completeSelf)
                    {
                        if (!this.hasCalledAbort)
                        {
                            this.hasCalledAbort = true;
                            this.Controller.Abort(reason);

                            // We need to get off this thread so we don't block the caller of abort
                            this.ScheduleTrackAndRaiseAborted(reason);
                        }
                    }
                }
                finally
                {
                    if (completeSelf)
                    {
                        this.NotifyOperationComplete(operation);
                    }
                }
            }
        }

        private void OnAbortWaitComplete(object state, TimeoutException exception)
        {
            if (exception != null)
            {
                // We swallow this exception because we were simply doing our best to get the lock.
                // Note that we won't proceed without the lock because we may have already succeeded
                // on another thread. Technically this abort call has failed.

                return;
            }

            var shouldRaise = false;
            var reason = (Exception)state;

            try
            {
                if (!this.hasCalledAbort)
                {
                    shouldRaise = true;
                    this.hasCalledAbort = true;
                    this.Controller.Abort(reason);
                }
            }
            finally
            {
                this.ForceNotifyOperationComplete();
            }

            if (shouldRaise)
            {
                // We call this from this thread because we've already had a thread switch
                this.TrackAndRaiseAborted(reason);
            }
        }

        private void ScheduleTrackAndRaiseAborted(Exception reason)
        {
            if (this.Controller.HasPendingTrackingRecords || this.Aborted != null)
            {
                ActionItem.Schedule(new Action<object>(this.TrackAndRaiseAborted), reason);
            }
        }

        // This is only ever called from an appropriate thread (not the thread that called abort
        // unless it was an internal abort). This method is called without the lock. We still
        // provide single threaded guarantees to the Controller because:
        // * No other call can ever enter the executor again once the state has switched to Aborted
        // * If this was an internal abort then the thread was fast pathing its way out of the
        // runtime and won't conflict
        private void TrackAndRaiseAborted(object state)
        {
            var reason = (Exception)state;

            if (this.Controller.HasPendingTrackingRecords)
            {
                try
                {
                    var result = this.Controller.BeginFlushTrackingRecords(ActivityDefaults.TrackingTimeout, Fx.ThunkCallback(new AsyncCallback(this.OnAbortTrackingComplete)), reason);

                    if (result.CompletedSynchronously)
                    {
                        this.Controller.EndFlushTrackingRecords(result);
                    }
                    else
                    {
                        return;
                    }
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    // We swallow any exception here because we are on the abort path and are doing
                    // a best effort to track this record.
                }
            }

            this.RaiseAborted(reason);
        }

        private void OnAbortTrackingComplete(IAsyncResult result)
        {
            if (result.CompletedSynchronously)
            {
                return;
            }

            var reason = (Exception)result.AsyncState;

            try
            {
                this.Controller.EndFlushTrackingRecords(result);
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }

                // We swallow any exception here because we are on the abort path and are doing a
                // best effort to track this record.
            }

            this.RaiseAborted(reason);
        }

        private void RaiseAborted(Exception reason)
        {
            if (this.invokeCompletedCallback == null)
            {
                var abortedHandler = this.Aborted;

                if (abortedHandler != null)
                {
                    try
                    {
                        this.handlerThreadId = Thread.CurrentThread.ManagedThreadId;
                        this.isInHandler = true;

                        abortedHandler(new WorkflowApplicationAbortedEventArgs(this, reason));
                    }
                    finally
                    {
                        this.isInHandler = false;
                    }
                }
            }
            else
            {
                this.invokeCompletedCallback();
            }

            if (TD.WorkflowInstanceAbortedIsEnabled())
            {
                TD.WorkflowInstanceAborted(this.Id.ToString(), reason);
            }
        }

        public void Terminate(string reason) => this.Terminate(reason, ActivityDefaults.AcquireLockTimeout);

        public void Terminate(Exception reason) => this.Terminate(reason, ActivityDefaults.AcquireLockTimeout);

        public void Terminate(string reason, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(reason));
            }

            this.Terminate(new WorkflowApplicationTerminatedException(reason, this.Id), timeout);
        }

        public void Terminate(Exception reason, TimeSpan timeout)
        {
            if (reason == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(reason));
            }

            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            var timeoutHelper = new TimeoutHelper(timeout);
            InstanceOperation operation = null;

            try
            {
                operation = new InstanceOperation();

                this.WaitForTurn(operation, timeoutHelper.RemainingTime());

                this.ValidateStateForTerminate();

                this.TerminateCore(reason);

                this.Controller.FlushTrackingRecords(timeoutHelper.RemainingTime());
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

        private void TerminateCore(Exception reason) => this.Controller.Terminate(reason);

        public IAsyncResult BeginTerminate(string reason, AsyncCallback callback, object state) => this.BeginTerminate(reason, ActivityDefaults.AcquireLockTimeout, callback, state);

        public IAsyncResult BeginTerminate(Exception reason, AsyncCallback callback, object state) => this.BeginTerminate(reason, ActivityDefaults.AcquireLockTimeout, callback, state);

        public IAsyncResult BeginTerminate(string reason, TimeSpan timeout, AsyncCallback callback, object state)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(reason));
            }

            return this.BeginTerminate(new WorkflowApplicationTerminatedException(reason, this.Id), timeout, callback, state);
        }

        public IAsyncResult BeginTerminate(Exception reason, TimeSpan timeout, AsyncCallback callback, object state)
        {
            if (reason == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(reason));
            }

            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            return TerminateAsyncResult.Create(this, reason, timeout, callback, state);
        }

        public void EndTerminate(IAsyncResult result) => TerminateAsyncResult.End(result);

        // called from the sync and async paths
        private void CancelCore()
        {
            // We only actually do any work if we haven't completed and we aren't unloaded.
            if (!this.hasRaisedCompleted && this.state != WorkflowApplicationState.Unloaded)
            {
                this.Controller.ScheduleCancel();

                // This is a loose check, but worst case scenario we call an extra, unnecessary Run
                if (!this.hasCalledRun && !this.hasRaisedCompleted)
                {
                    this.RunCore();
                }
            }
        }

        public void Cancel() => this.Cancel(ActivityDefaults.AcquireLockTimeout);

        public void Cancel(TimeSpan timeout)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            var timeoutHelper = new TimeoutHelper(timeout);

            InstanceOperation operation = null;

            try
            {
                operation = new InstanceOperation();

                this.WaitForTurn(operation, timeoutHelper.RemainingTime());

                this.ValidateStateForCancel();

                this.CancelCore();

                this.Controller.FlushTrackingRecords(timeoutHelper.RemainingTime());
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

        public IAsyncResult BeginCancel(AsyncCallback callback, object state) => this.BeginCancel(ActivityDefaults.AcquireLockTimeout, callback, state);

        public IAsyncResult BeginCancel(TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            return CancelAsyncResult.Create(this, timeout, callback, state);
        }

        public void EndCancel(IAsyncResult result) => CancelAsyncResult.End(result);

        // called on the Invoke path, this will go away when WorkflowInvoker implements
        // WorkflowInstance directly
        private static WorkflowApplication CreateInstance(Activity activity, IDictionary<string, object> inputs, WorkflowInstanceExtensionManager extensions, SynchronizationContext syncContext, Action invokeCompletedCallback)
        {
            // 1) Create the workflow instance
            var ambientTransaction = Transaction.Current;
            List<Handle> workflowExecutionProperties = null;

            if (ambientTransaction != null)
            {
                // no need for a NoPersistHandle since the ActivityExecutor performs a no-persist
                // zone as part of the RuntimeTransactionHandle processing
                workflowExecutionProperties = new List<Handle>(1)
                {
                    new RuntimeTransactionHandle(ambientTransaction)
                };
            }

            var instance = new WorkflowApplication(activity, inputs, workflowExecutionProperties)
            {
                SynchronizationContext = syncContext
            };

            var success = false;

            try
            {
                // 2) Take the executor lock before allowing extensions to be added
                instance.isBusy = true;

                // 3) Add extensions
                if (extensions != null)
                {
                    instance.extensions = extensions;
                }

                // 4) Setup miscellaneous state
                instance.invokeCompletedCallback = invokeCompletedCallback;

                success = true;
            }
            finally
            {
                if (!success)
                {
                    instance.isBusy = false;
                }
            }

            return instance;
        }

        private static void RunInstance(WorkflowApplication instance)
        {
            // We still have the lock because we took it in Create

            // first make sure we're ready to run
            instance.EnsureInitialized();

            // Shortcut path for resuming the instance
            instance.RunCore();

            instance.hasExecutionOccurredSinceLastIdle = true;
            instance.Controller.Run();
        }

        private static WorkflowApplication StartInvoke(Activity activity, IDictionary<string, object> inputs, WorkflowInstanceExtensionManager extensions, SynchronizationContext syncContext, Action invokeCompletedCallback, AsyncInvokeContext invokeContext)
        {
            var instance = CreateInstance(activity, inputs, extensions, syncContext, invokeCompletedCallback);
            if (invokeContext != null)
            {
                invokeContext.WorkflowApplication = instance;
            }
            RunInstance(instance);
            return instance;
        }

        internal static IDictionary<string, object> Invoke(Activity activity, IDictionary<string, object> inputs, WorkflowInstanceExtensionManager extensions, TimeSpan timeout)
        {
            Fx.Assert(activity != null, "Activity must not be null.");

            // Create the invoke synchronization context
            var syncContext = new PumpBasedSynchronizationContext(timeout);
            var instance = CreateInstance(activity, inputs, extensions, syncContext, new Action(syncContext.OnInvokeCompleted));
            // Wait for completion
            try
            {
                RunInstance(instance);
                syncContext.DoPump();
            }
            catch (TimeoutException)
            {
                instance.Abort(SR.AbortingDueToInstanceTimeout);
                throw;
            }

            Exception completionException = null;
            IDictionary<string, object> outputs = null;

            if (instance.Controller.State == WorkflowInstanceState.Aborted)
            {
                completionException = new WorkflowApplicationAbortedException(SR.DefaultAbortReason, instance.Controller.GetAbortReason());
            }
            else
            {
                Fx.Assert(instance.Controller.State == WorkflowInstanceState.Complete, "We should only get here when we are completed.");

                instance.Controller.GetCompletionState(out outputs, out completionException);
            }

            if (completionException != null)
            {
                throw FxTrace.Exception.AsError(completionException);
            }

            return outputs;
        }

        internal static IAsyncResult BeginInvoke(Activity activity, IDictionary<string, object> inputs, WorkflowInstanceExtensionManager extensions, TimeSpan timeout, SynchronizationContext syncContext, AsyncInvokeContext invokeContext, AsyncCallback callback, object state)
        {
            Fx.Assert(activity != null, "The activity must not be null.");

            return new InvokeAsyncResult(activity, inputs, extensions, timeout, syncContext, invokeContext, callback, state);
        }

        internal static IDictionary<string, object> EndInvoke(IAsyncResult result) => InvokeAsyncResult.End(result);

        public void Run() => this.Run(ActivityDefaults.AcquireLockTimeout);

        public void Run(TimeSpan timeout) => this.InternalRun(timeout, true);

        private void InternalRun(TimeSpan timeout, bool isUserRun)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            var timeoutHelper = new TimeoutHelper(timeout);
            InstanceOperation operation = null;

            try
            {
                operation = new InstanceOperation();

                this.WaitForTurn(operation, timeoutHelper.RemainingTime());

                this.ValidateStateForRun();

                if (isUserRun)
                {
                    // We set this to true here so that idle is raised regardless of whether the
                    // call to Run resulted in execution.
                    this.hasExecutionOccurredSinceLastIdle = true;
                }

                this.RunCore();

                this.Controller.FlushTrackingRecords(timeoutHelper.RemainingTime());
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

        private void RunCore()
        {
            if (!this.hasCalledRun)
            {
                this.hasCalledRun = true;
            }

            this.state = WorkflowApplicationState.Runnable;
        }

        public IAsyncResult BeginRun(AsyncCallback callback, object state) => this.BeginRun(ActivityDefaults.AcquireLockTimeout, callback, state);

        public IAsyncResult BeginRun(TimeSpan timeout, AsyncCallback callback, object state) => this.BeginInternalRun(timeout, true, callback, state);

        private IAsyncResult BeginInternalRun(TimeSpan timeout, bool isUserRun, AsyncCallback callback, object state)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            return RunAsyncResult.Create(this, isUserRun, timeout, callback, state);
        }

        public void EndRun(IAsyncResult result) => RunAsyncResult.End(result);

        // shared by Load/BeginLoad
        private bool IsLoadTransactionRequired() => base.GetExtensions<IPersistencePipelineModule>().Any(module => module.IsLoadTransactionRequired);

        private void CreatePersistenceManager()
        {
            var newManager = new PersistenceManager(this.InstanceStore, this.GetInstanceMetadata(), this.instanceId);
            this.SetPersistenceManager(newManager);
        }

        // shared by Load(WorkflowApplicationInstance)/BeginLoad*
        private void SetPersistenceManager(PersistenceManager newManager)
        {
            Fx.Assert(this.persistenceManager == null, "SetPersistenceManager should only be called once");

            // first register our extensions since we'll need them to construct the pipeline
            base.RegisterExtensionManager(this.extensions);
            this.persistenceManager = newManager;
        }

        // shared by Load/BeginLoad
        private PersistencePipeline ProcessInstanceValues(IDictionary<XName, InstanceValue> values, out object deserializedRuntimeState)
        {
            PersistencePipeline result = null;
            deserializedRuntimeState = ExtractRuntimeState(values, this.persistenceManager.InstanceId);

            if (this.HasPersistenceModule)
            {
                var modules = base.GetExtensions<IPersistencePipelineModule>();
                result = new PersistencePipeline(modules);
                result.SetLoadedValues(values);
            }

            return result;
        }

        private static ActivityExecutor ExtractRuntimeState(IDictionary<XName, InstanceValue> values, Guid instanceId)
        {
            if (values.TryGetValue(WorkflowNamespace.Workflow, out var value) && value.Value is ActivityExecutor result)
            {
                return result;
            }

            throw FxTrace.Exception.AsError(new InstancePersistenceException(SR.WorkflowInstanceNotFoundInStore(instanceId)));
        }

        public static void CreateDefaultInstanceOwner(InstanceStore instanceStore, WorkflowIdentity definitionIdentity, WorkflowIdentityFilter identityFilter) => CreateDefaultInstanceOwner(instanceStore, definitionIdentity, identityFilter, ActivityDefaults.OpenTimeout);

        public static void CreateDefaultInstanceOwner(InstanceStore instanceStore, WorkflowIdentity definitionIdentity, WorkflowIdentityFilter identityFilter, TimeSpan timeout)
        {
            if (instanceStore == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(instanceStore));
            }
            if (instanceStore.DefaultInstanceOwner != null)
            {
                throw FxTrace.Exception.Argument(nameof(instanceStore), SR.InstanceStoreHasDefaultOwner);
            }

            var command = GetCreateOwnerCommand(definitionIdentity, identityFilter);
            var commandResult = ExecuteInstanceCommandWithTemporaryHandle(instanceStore, command, timeout);
            instanceStore.DefaultInstanceOwner = commandResult.InstanceOwner;
        }

        public static IAsyncResult BeginCreateDefaultInstanceOwner(InstanceStore instanceStore, WorkflowIdentity definitionIdentity,
            WorkflowIdentityFilter identityFilter, AsyncCallback callback, object state) => BeginCreateDefaultInstanceOwner(instanceStore, definitionIdentity, identityFilter, ActivityDefaults.OpenTimeout, callback, state);

        public static IAsyncResult BeginCreateDefaultInstanceOwner(InstanceStore instanceStore, WorkflowIdentity definitionIdentity,
            WorkflowIdentityFilter identityFilter, TimeSpan timeout, AsyncCallback callback, object state)
        {
            if (instanceStore == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(instanceStore));
            }
            if (instanceStore.DefaultInstanceOwner != null)
            {
                throw FxTrace.Exception.Argument(nameof(instanceStore), SR.InstanceStoreHasDefaultOwner);
            }

            var command = GetCreateOwnerCommand(definitionIdentity, identityFilter);
            return new InstanceCommandWithTemporaryHandleAsyncResult(instanceStore, command, timeout, callback, state);
        }

        public static void EndCreateDefaultInstanceOwner(IAsyncResult asyncResult)
        {
            InstanceCommandWithTemporaryHandleAsyncResult.End(asyncResult, out var instanceStore, out var commandResult);
            instanceStore.DefaultInstanceOwner = commandResult.InstanceOwner;
        }

        public static void DeleteDefaultInstanceOwner(InstanceStore instanceStore) => DeleteDefaultInstanceOwner(instanceStore, ActivityDefaults.CloseTimeout);

        public static void DeleteDefaultInstanceOwner(InstanceStore instanceStore, TimeSpan timeout)
        {
            if (instanceStore == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(instanceStore));
            }
            if (instanceStore.DefaultInstanceOwner == null)
            {
                return;
            }

            var command = new DeleteWorkflowOwnerCommand();
            ExecuteInstanceCommandWithTemporaryHandle(instanceStore, command, timeout);
            instanceStore.DefaultInstanceOwner = null;
        }

        public static IAsyncResult BeginDeleteDefaultInstanceOwner(InstanceStore instanceStore, AsyncCallback callback, object state) => BeginDeleteDefaultInstanceOwner(instanceStore, ActivityDefaults.CloseTimeout, callback, state);

        public static IAsyncResult BeginDeleteDefaultInstanceOwner(InstanceStore instanceStore, TimeSpan timeout, AsyncCallback callback, object state)
        {
            if (instanceStore == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(instanceStore));
            }
            if (instanceStore.DefaultInstanceOwner == null)
            {
                return new CompletedAsyncResult(callback, state);
            }

            var command = new DeleteWorkflowOwnerCommand();
            return new InstanceCommandWithTemporaryHandleAsyncResult(instanceStore, command, timeout, callback, state);
        }

        public static void EndDeleteDefaultInstanceOwner(IAsyncResult asyncResult)
        {
            if (asyncResult is CompletedAsyncResult)
            {
                CompletedAsyncResult.End(asyncResult);
                return;
            }

            InstanceCommandWithTemporaryHandleAsyncResult.End(asyncResult, out var instanceStore, out var commandResult);
            instanceStore.DefaultInstanceOwner = null;
        }

        private static InstanceView ExecuteInstanceCommandWithTemporaryHandle(InstanceStore instanceStore, InstancePersistenceCommand command, TimeSpan timeout)
        {
            InstanceHandle temporaryHandle = null;
            try
            {
                temporaryHandle = instanceStore.CreateInstanceHandle();
                return instanceStore.Execute(temporaryHandle, command, timeout);
            }
            finally
            {
                if (temporaryHandle != null)
                {
                    temporaryHandle.Free();
                }
            }
        }

        private static CreateWorkflowOwnerWithIdentityCommand GetCreateOwnerCommand(WorkflowIdentity definitionIdentity, WorkflowIdentityFilter identityFilter)
        {
            if (!identityFilter.IsValid())
            {
                throw FxTrace.Exception.AsError(new ArgumentOutOfRangeException(nameof(identityFilter)));
            }
            if (definitionIdentity == null && identityFilter != WorkflowIdentityFilter.Any)
            {
                // This API isn't useful for null identity, because WFApp only adds a default
                // WorkflowHostType to instances with non-null identity.
                throw FxTrace.Exception.Argument(nameof(definitionIdentity), SR.CannotCreateOwnerWithoutIdentity);
            }
            return new CreateWorkflowOwnerWithIdentityCommand
            {
                InstanceOwnerMetadata =
                {
                    { WorkflowNamespace.WorkflowHostType, new InstanceValue(Workflow45Namespace.WorkflowApplication) },
                    { Workflow45Namespace.DefinitionIdentities, new InstanceValue(new Collection<WorkflowIdentity> { definitionIdentity }) },
                    { Workflow45Namespace.DefinitionIdentityFilter, new InstanceValue(identityFilter) },
                }
            };
        }

        public static WorkflowApplicationInstance GetRunnableInstance(InstanceStore instanceStore) => GetRunnableInstance(instanceStore, ActivityDefaults.LoadTimeout);

        public static WorkflowApplicationInstance GetRunnableInstance(InstanceStore instanceStore, TimeSpan timeout)
        {
            if (instanceStore == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(instanceStore));
            }
            TimeoutHelper.ThrowIfNegativeArgument(timeout);
            if (instanceStore.DefaultInstanceOwner == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.GetRunnableRequiresOwner));
            }

            var newManager = new PersistenceManager(instanceStore, null);
            return LoadCore(timeout, true, newManager);
        }

        public static IAsyncResult BeginGetRunnableInstance(InstanceStore instanceStore, AsyncCallback callback, object state) => BeginGetRunnableInstance(instanceStore, ActivityDefaults.LoadTimeout, callback, state);

        public static IAsyncResult BeginGetRunnableInstance(InstanceStore instanceStore, TimeSpan timeout, AsyncCallback callback, object state)
        {
            if (instanceStore == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(instanceStore));
            }
            TimeoutHelper.ThrowIfNegativeArgument(timeout);
            if (instanceStore.DefaultInstanceOwner == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.GetRunnableRequiresOwner));
            }

            var newManager = new PersistenceManager(instanceStore, null);
            return new LoadAsyncResult(null, newManager, true, timeout, callback, state);
        }

        public static WorkflowApplicationInstance EndGetRunnableInstance(IAsyncResult asyncResult) => LoadAsyncResult.EndAndCreateInstance(asyncResult);

        public static WorkflowApplicationInstance GetInstance(Guid instanceId, InstanceStore instanceStore) => GetInstance(instanceId, instanceStore, ActivityDefaults.LoadTimeout);

        public static WorkflowApplicationInstance GetInstance(Guid instanceId, InstanceStore instanceStore, TimeSpan timeout)
        {
            if (instanceId == Guid.Empty)
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(instanceId));
            }
            if (instanceStore == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(instanceStore));
            }
            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            var newManager = new PersistenceManager(instanceStore, null, instanceId);
            return LoadCore(timeout, false, newManager);
        }

        public static IAsyncResult BeginGetInstance(Guid instanceId, InstanceStore instanceStore, AsyncCallback callback, object state) => BeginGetInstance(instanceId, instanceStore, ActivityDefaults.LoadTimeout, callback, state);

        public static IAsyncResult BeginGetInstance(Guid instanceId, InstanceStore instanceStore, TimeSpan timeout, AsyncCallback callback, object state)
        {
            if (instanceId == Guid.Empty)
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(instanceId));
            }
            if (instanceStore == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(instanceStore));
            }
            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            var newManager = new PersistenceManager(instanceStore, null, instanceId);
            return new LoadAsyncResult(null, newManager, false, timeout, callback, state);
        }

        public static WorkflowApplicationInstance EndGetInstance(IAsyncResult asyncResult) => LoadAsyncResult.EndAndCreateInstance(asyncResult);

#if NET45
        public void Load(WorkflowApplicationInstance instance)
        {
            Load(instance, ActivityDefaults.LoadTimeout);
        }

        public void Load(WorkflowApplicationInstance instance, TimeSpan timeout)
        {
            Load(instance, null, timeout);
        }

        public void Load(WorkflowApplicationInstance instance, DynamicUpdateMap updateMap)
        {
            Load(instance, updateMap, ActivityDefaults.LoadTimeout);
        }

        public void Load(WorkflowApplicationInstance instance, DynamicUpdateMap updateMap, TimeSpan timeout)
        {
            ThrowIfAborted();
            ThrowIfReadOnly(); // only allow a single Load() or Run()
            if (instance == null)
            {
                throw FxTrace.Exception.ArgumentNull("instance");
            }

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            if (this.instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
            }
            if (this.initialWorkflowArguments != null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotUseInputsWithLoad));
            }
            if (this.InstanceStore != null && this.InstanceStore != instance.InstanceStore)
            {
                throw FxTrace.Exception.Argument("instance", SR.InstanceStoreDoesntMatchWorkflowApplication);
            }

            instance.MarkAsLoaded();

            InstanceOperation operation = new InstanceOperation { RequiresInitialized = false };

            try
            {
                TimeoutHelper timeoutHelper = new TimeoutHelper(timeout);
                WaitForTurn(operation, timeoutHelper.RemainingTime());

                ValidateStateForLoad();

                this.instanceId = instance.InstanceId;
                this.instanceIdSet = true;
                if (this.instanceStore == null)
                {
                    this.instanceStore = instance.InstanceStore;
                }

                PersistenceManager newManager = (PersistenceManager)instance.PersistenceManager;
                newManager.SetInstanceMetadata(GetInstanceMetadata());
                SetPersistenceManager(newManager);

                LoadCore(updateMap, timeoutHelper, false, instance.Values);
            }
            finally
            {
                NotifyOperationComplete(operation);
            }
        }
#else

        public void Load(WorkflowApplicationInstance instance) => this.Load(instance, ActivityDefaults.LoadTimeout);

        public void Load(WorkflowApplicationInstance instance, TimeSpan timeout)
        {
            this.ThrowIfAborted();
            this.ThrowIfReadOnly(); // only allow a single Load() or Run()
            if (instance == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(instance));
            }

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            if (this.instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
            }
            if (this.initialWorkflowArguments != null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotUseInputsWithLoad));
            }
            if (this.InstanceStore != null && this.InstanceStore != instance.InstanceStore)
            {
                throw FxTrace.Exception.Argument(nameof(instance), SR.InstanceStoreDoesntMatchWorkflowApplication);
            }

            instance.MarkAsLoaded();

            var operation = new InstanceOperation { RequiresInitialized = false };

            try
            {
                var timeoutHelper = new TimeoutHelper(timeout);
                this.WaitForTurn(operation, timeoutHelper.RemainingTime());

                this.ValidateStateForLoad();

                this.instanceId = instance.InstanceId;
                this.instanceIdSet = true;
                if (this.instanceStore == null)
                {
                    this.instanceStore = instance.InstanceStore;
                }

                var newManager = (PersistenceManager)instance.PersistenceManager;
                newManager.SetInstanceMetadata(this.GetInstanceMetadata());
                this.SetPersistenceManager(newManager);
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

#endif

        public void LoadRunnableInstance() => this.LoadRunnableInstance(ActivityDefaults.LoadTimeout);

        public void LoadRunnableInstance(TimeSpan timeout)
        {
            this.ThrowIfReadOnly(); // only allow a single Load() or Run()

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            if (this.InstanceStore == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.LoadingWorkflowApplicationRequiresInstanceStore));
            }
            if (this.instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
            }
            if (this.initialWorkflowArguments != null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotUseInputsWithLoad));
            }
            if (this.persistenceManager != null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.TryLoadRequiresOwner));
            }

            var operation = new InstanceOperation { RequiresInitialized = false };

            try
            {
                var timeoutHelper = new TimeoutHelper(timeout);
                this.WaitForTurn(operation, timeoutHelper.RemainingTime());

                this.ValidateStateForLoad();

                this.RegisterExtensionManager(this.extensions);
                this.persistenceManager = new PersistenceManager(this.InstanceStore, this.GetInstanceMetadata());

                if (!this.persistenceManager.IsInitialized)
                {
                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.TryLoadRequiresOwner));
                }

#if NET45
                LoadCore(null, timeoutHelper, true);
#else
                this.LoadCore(timeoutHelper, true);
#endif
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

        public void Load(Guid instanceId) => this.Load(instanceId, ActivityDefaults.LoadTimeout);

        public void Load(Guid instanceId, TimeSpan timeout)
        {
            this.ThrowIfAborted();
            this.ThrowIfReadOnly(); // only allow a single Load() or Run()
            if (instanceId == Guid.Empty)
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(instanceId));
            }

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            if (this.InstanceStore == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.LoadingWorkflowApplicationRequiresInstanceStore));
            }
            if (this.instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
            }
            if (this.initialWorkflowArguments != null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotUseInputsWithLoad));
            }

            var operation = new InstanceOperation { RequiresInitialized = false };

            try
            {
                var timeoutHelper = new TimeoutHelper(timeout);
                this.WaitForTurn(operation, timeoutHelper.RemainingTime());

                this.ValidateStateForLoad();

                this.instanceId = instanceId;
                this.instanceIdSet = true;

                this.CreatePersistenceManager();
#if NET45
                LoadCore(null, timeoutHelper, false);
#else
                this.LoadCore(timeoutHelper, false);
#endif
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

#if NET45
        void LoadCore(DynamicUpdateMap updateMap, TimeoutHelper timeoutHelper, bool loadAny, IDictionary<XName, InstanceValue> values = null)
#else

        private void LoadCore(TimeoutHelper timeoutHelper, bool loadAny, IDictionary<XName, InstanceValue> values = null)
#endif
        {
            if (values == null)
            {
                if (!this.persistenceManager.IsInitialized)
                {
                    this.persistenceManager.Initialize(this.DefinitionIdentity, timeoutHelper.RemainingTime());
                }
            }
            else
            {
                Fx.Assert(this.persistenceManager.IsInitialized, "Caller should have initialized Persistence Manager");
                Fx.Assert(this.instanceIdSet, "Caller should have set InstanceId");
            }

            PersistencePipeline pipeline = null;
            WorkflowPersistenceContext context = null;
            TransactionScope scope = null;
            var success = false;
            Exception abortReasonInnerException = null;
            try
            {
                InitializePersistenceContext(this.IsLoadTransactionRequired(), timeoutHelper, out context, out scope);

                if (values == null)
                {
                    values = LoadValues(this.persistenceManager, timeoutHelper, loadAny);
                    if (loadAny)
                    {
                        if (this.instanceIdSet)
                        {
                            throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
                        }

                        this.instanceId = this.persistenceManager.InstanceId;
                        this.instanceIdSet = true;
                    }
                }
                pipeline = this.ProcessInstanceValues(values, out var deserializedRuntimeState);

                if (pipeline != null)
                {
                    try
                    {
                        this.persistencePipelineInUse = pipeline;

                        // Need to ensure that either we see the Aborted state, AbortInstance sees
                        // us, or both.
                        Thread.MemoryBarrier();

                        if (this.state == WorkflowApplicationState.Aborted)
                        {
                            throw FxTrace.Exception.AsError(new OperationCanceledException(SR.DefaultAbortReason));
                        }

                        pipeline.EndLoad(pipeline.BeginLoad(timeoutHelper.RemainingTime(), null, null));
                    }
                    finally
                    {
                        this.persistencePipelineInUse = null;
                    }
                }

                try
                {
#if NET45
                    base.Initialize(deserializedRuntimeState, updateMap);
                    if (updateMap != null)
                    {
                        UpdateInstanceMetadata();
                    }
#else
                    base.Initialize(deserializedRuntimeState);
#endif
                }
#if NET45
                catch (InstanceUpdateException e)
                {
                    abortReasonInnerException = e;
                    throw;
                }
#endif
                catch (VersionMismatchException e)
                {
                    abortReasonInnerException = e;
                    throw;
                }

                success = true;
            }
            finally
            {
                CompletePersistenceContext(context, scope, success);
                if (!success)
                {
                    this.AbortDueToException(abortReasonInnerException);
                }
            }

            if (pipeline != null)
            {
                pipeline.Publish();
            }
        }

        private void AbortDueToException(Exception e)
        {
#if NET45
            if (e is InstanceUpdateException)
            {
                this.Abort(SR.AbortingDueToDynamicUpdateFailure, e);
            }
            else
#endif
            if (e is VersionMismatchException)
            {
                this.Abort(SR.AbortingDueToVersionMismatch, e);
            }
            else
            {
                this.Abort(SR.AbortingDueToLoadFailure);
            }
        }

        private static WorkflowApplicationInstance LoadCore(TimeSpan timeout, bool loadAny, PersistenceManager persistenceManager)
        {
            var timeoutHelper = new TimeoutHelper(timeout);

            if (!persistenceManager.IsInitialized)
            {
                persistenceManager.Initialize(unknownIdentity, timeoutHelper.RemainingTime());
            }

            WorkflowPersistenceContext context = null;
            TransactionScope scope = null;
            WorkflowApplicationInstance result = null;
            try
            {
                InitializePersistenceContext(false, timeoutHelper, out context, out scope);

                var values = LoadValues(persistenceManager, timeoutHelper, loadAny);
                var deserializedRuntimeState = ExtractRuntimeState(values, persistenceManager.InstanceId);
                result = new WorkflowApplicationInstance(persistenceManager, values, deserializedRuntimeState.WorkflowIdentity);
            }
            finally
            {
                var success = (result != null);
                CompletePersistenceContext(context, scope, success);
                if (!success)
                {
                    persistenceManager.Abort();
                }
            }

            return result;
        }

        private static void InitializePersistenceContext(bool isTransactionRequired, TimeoutHelper timeoutHelper,
            out WorkflowPersistenceContext context, out TransactionScope scope)
        {
            context = new WorkflowPersistenceContext(isTransactionRequired, timeoutHelper.OriginalTimeout);
            scope = TransactionHelper.CreateTransactionScope(context.PublicTransaction);
        }

        private static void CompletePersistenceContext(WorkflowPersistenceContext context, TransactionScope scope, bool success)
        {
            // Clean up the transaction scope regardless of failure
            TransactionHelper.CompleteTransactionScope(ref scope);

            if (context != null)
            {
                if (success)
                {
                    context.Complete();
                }
                else
                {
                    context.Abort();
                }
            }
        }

        private static IDictionary<XName, InstanceValue> LoadValues(PersistenceManager persistenceManager, TimeoutHelper timeoutHelper, bool loadAny)
        {
            IDictionary<XName, InstanceValue> values;
            if (loadAny)
            {
                if (!persistenceManager.TryLoad(timeoutHelper.RemainingTime(), out values))
                {
                    throw FxTrace.Exception.AsError(new InstanceNotReadyException(SR.NoRunnableInstances));
                }
            }
            else
            {
                values = persistenceManager.Load(timeoutHelper.RemainingTime());
            }

            return values;
        }

        internal static void DiscardInstance(PersistenceManagerBase persistanceManager, TimeSpan timeout)
        {
            var manager = (PersistenceManager)persistanceManager;
            var timeoutHelper = new TimeoutHelper(timeout);
            UnlockInstance(manager, timeoutHelper);
        }

        internal static IAsyncResult BeginDiscardInstance(PersistenceManagerBase persistanceManager, TimeSpan timeout, AsyncCallback callback, object state)
        {
            var manager = (PersistenceManager)persistanceManager;
            var timeoutHelper = new TimeoutHelper(timeout);
            return new UnlockInstanceAsyncResult(manager, timeoutHelper, callback, state);
        }

        internal static void EndDiscardInstance(IAsyncResult asyncResult) => UnlockInstanceAsyncResult.End(asyncResult);

        private static void UnlockInstance(PersistenceManager persistenceManager, TimeoutHelper timeoutHelper)
        {
            try
            {
                if (persistenceManager.OwnerWasCreated)
                {
                    // if the owner was created by this WorkflowApplication, delete it. This
                    // implicitly unlocks the instance.
                    persistenceManager.DeleteOwner(timeoutHelper.RemainingTime());
                }
                else
                {
                    persistenceManager.Unlock(timeoutHelper.RemainingTime());
                }
            }
            finally
            {
                persistenceManager.Abort();
            }
        }

#if NET45
        internal static IList<ActivityBlockingUpdate> GetActivitiesBlockingUpdate(WorkflowApplicationInstance instance, DynamicUpdateMap updateMap)
        {
            object deserializedRuntimeState = ExtractRuntimeState(instance.Values, instance.InstanceId);
            return WorkflowInstance.GetActivitiesBlockingUpdate(deserializedRuntimeState, updateMap);
        }
#endif

        public IAsyncResult BeginLoadRunnableInstance(AsyncCallback callback, object state) => this.BeginLoadRunnableInstance(ActivityDefaults.LoadTimeout, callback, state);

        public IAsyncResult BeginLoadRunnableInstance(TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.ThrowIfReadOnly(); // only allow a single Load() or Run()

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            if (this.InstanceStore == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.LoadingWorkflowApplicationRequiresInstanceStore));
            }
            if (this.instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
            }
            if (this.initialWorkflowArguments != null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotUseInputsWithLoad));
            }
            if (this.persistenceManager != null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.TryLoadRequiresOwner));
            }

            var newManager = new PersistenceManager(this.InstanceStore, this.GetInstanceMetadata());
            if (!newManager.IsInitialized)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.TryLoadRequiresOwner));
            }

            return new LoadAsyncResult(this, newManager, true, timeout, callback, state);
        }

        public IAsyncResult BeginLoad(Guid instanceId, AsyncCallback callback, object state) => this.BeginLoad(instanceId, ActivityDefaults.LoadTimeout, callback, state);

        public IAsyncResult BeginLoad(Guid instanceId, TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.ThrowIfAborted();
            this.ThrowIfReadOnly(); // only allow a single Load() or Run()
            if (instanceId == Guid.Empty)
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(instanceId));
            }

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            if (this.InstanceStore == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.LoadingWorkflowApplicationRequiresInstanceStore));
            }
            if (this.instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
            }
            if (this.initialWorkflowArguments != null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotUseInputsWithLoad));
            }

            var newManager = new PersistenceManager(this.InstanceStore, this.GetInstanceMetadata(), instanceId);

            return new LoadAsyncResult(this, newManager, false, timeout, callback, state);
        }

#if NET45
        public IAsyncResult BeginLoad(WorkflowApplicationInstance instance, AsyncCallback callback, object state)
        {
            return BeginLoad(instance, null, ActivityDefaults.LoadTimeout, callback, state);
        }

        public IAsyncResult BeginLoad(WorkflowApplicationInstance instance, TimeSpan timeout,
            AsyncCallback callback, object state)
        {
            return BeginLoad(instance, null, timeout, callback, state);
        }

        public IAsyncResult BeginLoad(WorkflowApplicationInstance instance, DynamicUpdateMap updateMap,
            AsyncCallback callback, object state)
        {
            return BeginLoad(instance, updateMap, ActivityDefaults.LoadTimeout, callback, state);
        }

        public IAsyncResult BeginLoad(WorkflowApplicationInstance instance, DynamicUpdateMap updateMap, TimeSpan timeout,
            AsyncCallback callback, object state)
        {
            ThrowIfAborted();
            ThrowIfReadOnly(); // only allow a single Load() or Run()
            if (instance == null)
            {
                throw FxTrace.Exception.ArgumentNull("instance");
            }

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            if (this.instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
            }
            if (this.initialWorkflowArguments != null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotUseInputsWithLoad));
            }
            if (this.InstanceStore != null && this.InstanceStore != instance.InstanceStore)
            {
                throw FxTrace.Exception.Argument("instance", SR.InstanceStoreDoesntMatchWorkflowApplication);
            }

            instance.MarkAsLoaded();
            PersistenceManager newManager = (PersistenceManager)instance.PersistenceManager;
            newManager.SetInstanceMetadata(GetInstanceMetadata());

            return new LoadAsyncResult(this, newManager, instance.Values, updateMap, timeout, callback, state);
        }
#else

        public IAsyncResult BeginLoad(WorkflowApplicationInstance instance, AsyncCallback callback, object state) => this.BeginLoad(instance, ActivityDefaults.LoadTimeout, callback, state);

        public IAsyncResult BeginLoad(WorkflowApplicationInstance instance, TimeSpan timeout,
            AsyncCallback callback, object state)
        {
            this.ThrowIfAborted();
            this.ThrowIfReadOnly(); // only allow a single Load() or Run()
            if (instance == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(instance));
            }

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            if (this.instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
            }
            if (this.initialWorkflowArguments != null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotUseInputsWithLoad));
            }
            if (this.InstanceStore != null && this.InstanceStore != instance.InstanceStore)
            {
                throw FxTrace.Exception.Argument(nameof(instance), SR.InstanceStoreDoesntMatchWorkflowApplication);
            }

            instance.MarkAsLoaded();
            var newManager = (PersistenceManager)instance.PersistenceManager;
            newManager.SetInstanceMetadata(this.GetInstanceMetadata());

            return new LoadAsyncResult(this, newManager, instance.Values, timeout, callback, state);
        }

#endif

        public void EndLoad(IAsyncResult result) => LoadAsyncResult.End(result);

        public void EndLoadRunnableInstance(IAsyncResult result) => LoadAsyncResult.End(result);

        protected override void OnNotifyUnhandledException(Exception exception, Activity exceptionSource, string exceptionSourceInstanceId)
        {
            var done = true;

            try
            {
                Exception abortException = null;

                try
                {
                    if (unhandledExceptionHandler == null)
                    {
                        unhandledExceptionHandler = new UnhandledExceptionEventHandler();
                    }

                    done = unhandledExceptionHandler.Run(this, exception, exceptionSource, exceptionSourceInstanceId);
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    abortException = e;
                }

                if (abortException != null)
                {
                    this.AbortInstance(abortException, true);
                }
            }
            finally
            {
                if (done)
                {
                    this.OnNotifyPaused();
                }
            }
        }

        private IAsyncResult BeginInternalPersist(PersistenceOperation operation, TimeSpan timeout, bool isInternalPersist, AsyncCallback callback, object state) => new UnloadOrPersistAsyncResult(this, timeout, operation, true, isInternalPersist, callback, state);

        private void EndInternalPersist(IAsyncResult result) => UnloadOrPersistAsyncResult.End(result);

        private void TrackPersistence(PersistenceOperation operation)
        {
            if (this.Controller.TrackingEnabled)
            {
                if (operation == PersistenceOperation.Complete)
                {
                    this.Controller.Track(new WorkflowInstanceRecord(this.Id, this.WorkflowDefinition.DisplayName, WorkflowInstanceStates.Deleted, this.DefinitionIdentity));
                }
                else if (operation == PersistenceOperation.Unload)
                {
                    this.Controller.Track(new WorkflowInstanceRecord(this.Id, this.WorkflowDefinition.DisplayName, WorkflowInstanceStates.Unloaded, this.DefinitionIdentity));
                }
                else
                {
                    this.Controller.Track(new WorkflowInstanceRecord(this.Id, this.WorkflowDefinition.DisplayName, WorkflowInstanceStates.Persisted, this.DefinitionIdentity));
                }
            }
        }

        private void PersistCore(ref TimeoutHelper timeoutHelper, PersistenceOperation operation)
        {
            if (this.HasPersistenceProvider)
            {
                if (!this.persistenceManager.IsInitialized)
                {
                    this.persistenceManager.Initialize(this.DefinitionIdentity, timeoutHelper.RemainingTime());
                }
                if (!this.persistenceManager.IsLocked && Transaction.Current != null)
                {
                    this.persistenceManager.EnsureReadyness(timeoutHelper.RemainingTime());
                }

                // Do the tracking before preparing in case the tracking data is being pushed into
                // an extension and persisted transactionally with the instance state.
                this.TrackPersistence(operation);

                this.Controller.FlushTrackingRecords(timeoutHelper.RemainingTime());
            }

            var success = false;
            WorkflowPersistenceContext context = null;
            TransactionScope scope = null;

            try
            {
                IDictionary<XName, InstanceValue> data = null;
                PersistencePipeline pipeline = null;
                if (this.HasPersistenceModule)
                {
                    var modules = base.GetExtensions<IPersistencePipelineModule>();
                    pipeline = new PersistencePipeline(modules, PersistenceManager.GenerateInitialData(this));
                    pipeline.Collect();
                    pipeline.Map();
                    data = pipeline.Values;
                }

                if (this.HasPersistenceProvider)
                {
                    if (data == null)
                    {
                        data = PersistenceManager.GenerateInitialData(this);
                    }

                    if (context == null)
                    {
                        Fx.Assert(scope == null, "Should not have been able to set up a scope.");
                        InitializePersistenceContext(pipeline != null && pipeline.IsSaveTransactionRequired, timeoutHelper, out context, out scope);
                    }

                    this.persistenceManager.Save(data, operation, timeoutHelper.RemainingTime());
                }

                if (pipeline != null)
                {
                    if (context == null)
                    {
                        Fx.Assert(scope == null, "Should not have been able to set up a scope if we had no context.");
                        InitializePersistenceContext(pipeline.IsSaveTransactionRequired, timeoutHelper, out context, out scope);
                    }

                    try
                    {
                        this.persistencePipelineInUse = pipeline;

                        // Need to ensure that either we see the Aborted state, AbortInstance sees
                        // us, or both.
                        Thread.MemoryBarrier();

                        if (this.state == WorkflowApplicationState.Aborted)
                        {
                            throw FxTrace.Exception.AsError(new OperationCanceledException(SR.DefaultAbortReason));
                        }

                        pipeline.EndSave(pipeline.BeginSave(timeoutHelper.RemainingTime(), null, null));
                    }
                    finally
                    {
                        this.persistencePipelineInUse = null;
                    }
                }

                success = true;
            }
            finally
            {
                CompletePersistenceContext(context, scope, success);

                if (success)
                {
                    if (operation != PersistenceOperation.Save)
                    {
                        // Stop execution if we've given up the instance lock
                        this.state = WorkflowApplicationState.Paused;

                        if (TD.WorkflowApplicationUnloadedIsEnabled())
                        {
                            TD.WorkflowApplicationUnloaded(this.Id.ToString());
                        }
                    }
                    else
                    {
                        if (TD.WorkflowApplicationPersistedIsEnabled())
                        {
                            TD.WorkflowApplicationPersisted(this.Id.ToString());
                        }
                    }

                    if (operation == PersistenceOperation.Complete || operation == PersistenceOperation.Unload)
                    {
                        // We did a Delete or Unload, so if we have a persistence provider, tell it
                        // to delete the owner.
                        if (this.HasPersistenceProvider && this.persistenceManager.OwnerWasCreated)
                        {
                            // This will happen to be under the caller's transaction, if there is
                            // one. TODO, 124600, suppress the transaction
                            this.persistenceManager.DeleteOwner(timeoutHelper.RemainingTime());
                        }

                        this.MarkUnloaded();
                    }
                }
            }
        }

        [Fx.Tag.InheritThrows(From = "Unload")]
        public void Persist() => this.Persist(ActivityDefaults.SaveTimeout);

        [Fx.Tag.InheritThrows(From = "Unload")]
        public void Persist(TimeSpan timeout)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            var timeoutHelper = new TimeoutHelper(timeout);

            var operation = new RequiresPersistenceOperation();

            try
            {
                this.WaitForTurn(operation, timeoutHelper.RemainingTime());

                this.ValidateStateForPersist();

                this.PersistCore(ref timeoutHelper, PersistenceOperation.Save);
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

        [Fx.Tag.InheritThrows(From = "Unload")]
        public IAsyncResult BeginPersist(AsyncCallback callback, object state) => this.BeginPersist(ActivityDefaults.SaveTimeout, callback, state);

        [Fx.Tag.InheritThrows(From = "Unload")]
        public IAsyncResult BeginPersist(TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            return new UnloadOrPersistAsyncResult(this, timeout, PersistenceOperation.Save, false, false, callback, state);
        }

        [Fx.Tag.InheritThrows(From = "Unload")]
        public void EndPersist(IAsyncResult result) => UnloadOrPersistAsyncResult.End(result);

        // called from WorkflowApplicationIdleEventArgs
        internal ReadOnlyCollection<BookmarkInfo> GetBookmarksForIdle() => this.Controller.GetBookmarks();

        public ReadOnlyCollection<BookmarkInfo> GetBookmarks() => this.GetBookmarks(ActivityDefaults.ResumeBookmarkTimeout);

        public ReadOnlyCollection<BookmarkInfo> GetBookmarks(TimeSpan timeout)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            var operation = new InstanceOperation();

            try
            {
                this.WaitForTurn(operation, timeout);

                this.ValidateStateForGetAllBookmarks();

                return this.Controller.GetBookmarks();
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

        protected internal override IAsyncResult OnBeginPersist(AsyncCallback callback, object state) => this.BeginInternalPersist(PersistenceOperation.Save, ActivityDefaults.InternalSaveTimeout, true, callback, state);

        protected internal override void OnEndPersist(IAsyncResult result) => this.EndInternalPersist(result);

        protected internal override IAsyncResult OnBeginAssociateKeys(ICollection<InstanceKey> keys, AsyncCallback callback, object state) => throw Fx.AssertAndThrow("WorkflowApplication is sealed with CanUseKeys as false, so WorkflowInstance should not call OnBeginAssociateKeys.");

        protected internal override void OnEndAssociateKeys(IAsyncResult result) => throw Fx.AssertAndThrow("WorkflowApplication is sealed with CanUseKeys as false, so WorkflowInstance should not call OnEndAssociateKeys.");

        protected internal override void OnDisassociateKeys(ICollection<InstanceKey> keys) => throw Fx.AssertAndThrow("WorkflowApplication is sealed with CanUseKeys as false, so WorkflowInstance should not call OnDisassociateKeys.");

        private bool AreBookmarksInvalid(out BookmarkResumptionResult result)
        {
            if (this.hasRaisedCompleted)
            {
                result = BookmarkResumptionResult.NotFound;
                return true;
            }
            else if (this.state == WorkflowApplicationState.Unloaded || this.state == WorkflowApplicationState.Aborted)
            {
                result = BookmarkResumptionResult.NotReady;
                return true;
            }

            result = BookmarkResumptionResult.Success;
            return false;
        }

        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public BookmarkResumptionResult ResumeBookmark(string bookmarkName, object value)
        {
            if (string.IsNullOrEmpty(bookmarkName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(bookmarkName));
            }

            return this.ResumeBookmark(new Bookmark(bookmarkName), value);
        }

        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public BookmarkResumptionResult ResumeBookmark(Bookmark bookmark, object value) => this.ResumeBookmark(bookmark, value, ActivityDefaults.ResumeBookmarkTimeout);

        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public BookmarkResumptionResult ResumeBookmark(string bookmarkName, object value, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(bookmarkName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(bookmarkName));
            }

            return this.ResumeBookmark(new Bookmark(bookmarkName), value, timeout);
        }

        [Fx.Tag.InheritThrows(From = "BeginResumeBookmark", FromDeclaringType = typeof(WorkflowInstance))]
        public BookmarkResumptionResult ResumeBookmark(Bookmark bookmark, object value, TimeSpan timeout)
        {
            TimeoutHelper.ThrowIfNegativeArgument(timeout);
            this.ThrowIfHandlerThread();
            var timeoutHelper = new TimeoutHelper(timeout);

            InstanceOperation operation = new RequiresIdleOperation();
            BookmarkResumptionResult result;
            var pendedUnenqueued = false;

            try
            {
                // This is a loose check, but worst case scenario we call an extra, unnecessary Run
                if (!this.hasCalledRun)
                {
                    // Increment the pending unenqueued count so we don't raise idle in the time
                    // between when the Run completes and when we enqueue our InstanceOperation.
                    pendedUnenqueued = true;
                    this.IncrementPendingUnenqueud();

                    this.InternalRun(timeoutHelper.RemainingTime(), false);
                }

                do
                {
                    InstanceOperation nextOperation = null;

                    try
                    {
                        // Need to enqueue and wait for turn as two separate steps, so that OnQueued
                        // always gets called and we make sure to decrement the pendingUnenqueued counter
                        this.WaitForTurn(operation, timeoutHelper.RemainingTime());

                        if (pendedUnenqueued)
                        {
                            this.DecrementPendingUnenqueud();
                            pendedUnenqueued = false;
                        }

                        if (this.AreBookmarksInvalid(out result))
                        {
                            return result;
                        }

                        result = this.ResumeBookmarkCore(bookmark, value);

                        if (result == BookmarkResumptionResult.Success)
                        {
                            this.Controller.FlushTrackingRecords(timeoutHelper.RemainingTime());
                        }
                        else if (result == BookmarkResumptionResult.NotReady)
                        {
                            nextOperation = new DeferredRequiresIdleOperation();
                        }
                    }
                    finally
                    {
                        this.NotifyOperationComplete(operation);
                    }

                    operation = nextOperation;
                } while (operation != null);

                return result;
            }
            finally
            {
                if (pendedUnenqueued)
                {
                    this.DecrementPendingUnenqueud();
                }
            }
        }

        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public IAsyncResult BeginResumeBookmark(string bookmarkName, object value, AsyncCallback callback, object state)
        {
            if (string.IsNullOrEmpty(bookmarkName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(bookmarkName));
            }

            return this.BeginResumeBookmark(new Bookmark(bookmarkName), value, callback, state);
        }

        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public IAsyncResult BeginResumeBookmark(string bookmarkName, object value, TimeSpan timeout, AsyncCallback callback, object state)
        {
            if (string.IsNullOrEmpty(bookmarkName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(bookmarkName));
            }

            return this.BeginResumeBookmark(new Bookmark(bookmarkName), value, timeout, callback, state);
        }

        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public IAsyncResult BeginResumeBookmark(Bookmark bookmark, object value, AsyncCallback callback, object state) => this.BeginResumeBookmark(bookmark, value, ActivityDefaults.ResumeBookmarkTimeout, callback, state);

        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public IAsyncResult BeginResumeBookmark(Bookmark bookmark, object value, TimeSpan timeout, AsyncCallback callback, object state)
        {
            TimeoutHelper.ThrowIfNegativeArgument(timeout);
            this.ThrowIfHandlerThread();

            return new ResumeBookmarkAsyncResult(this, bookmark, value, timeout, callback, state);
        }

        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public BookmarkResumptionResult EndResumeBookmark(IAsyncResult result) => ResumeBookmarkAsyncResult.End(result);

        protected internal override IAsyncResult OnBeginResumeBookmark(Bookmark bookmark, object value, TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.ThrowIfHandlerThread();
            return new ResumeBookmarkAsyncResult(this, bookmark, value, true, timeout, callback, state);
        }

        protected internal override BookmarkResumptionResult OnEndResumeBookmark(IAsyncResult result) => ResumeBookmarkAsyncResult.End(result);

        private BookmarkResumptionResult ResumeBookmarkCore(Bookmark bookmark, object value)
        {
            var result = this.Controller.ScheduleBookmarkResumption(bookmark, value);

            if (result == BookmarkResumptionResult.Success)
            {
                this.RunCore();
            }

            return result;
        }

        // Returns true if successful, false otherwise
        private bool RaiseIdleEvent()
        {
            if (TD.WorkflowApplicationIdledIsEnabled())
            {
                TD.WorkflowApplicationIdled(this.Id.ToString());
            }

            Exception abortException = null;

            try
            {
                var idleHandler = this.Idle;

                if (idleHandler != null)
                {
                    this.handlerThreadId = Thread.CurrentThread.ManagedThreadId;
                    this.isInHandler = true;

                    idleHandler(new WorkflowApplicationIdleEventArgs(this));
                }
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }

                abortException = e;
            }
            finally
            {
                this.isInHandler = false;
            }

            if (abortException != null)
            {
                this.AbortInstance(abortException, true);
                return false;
            }

            return true;
        }

        private void MarkUnloaded()
        {
            this.state = WorkflowApplicationState.Unloaded;

            // don't abort completed instances
            if (this.Controller.State != WorkflowInstanceState.Complete)
            {
                this.Controller.Abort();
            }
            else
            {
                base.DisposeExtensions();
            }

            Exception abortException = null;

            try
            {
                var handler = this.Unloaded;

                if (handler != null)
                {
                    this.handlerThreadId = Thread.CurrentThread.ManagedThreadId;
                    this.isInHandler = true;

                    handler(new WorkflowApplicationEventArgs(this));
                }
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }

                abortException = e;
            }
            finally
            {
                this.isInHandler = false;
            }

            if (abortException != null)
            {
                this.AbortInstance(abortException, true);
            }
        }

        [Fx.Tag.Throws(typeof(WorkflowApplicationException), "The WorkflowApplication is in a state for which unloading is not valid.  The specific subclass denotes which state the instance is in.")]
        [Fx.Tag.Throws(typeof(InstancePersistenceException), "Something went wrong during persistence, but persistence can be retried.")]
        [Fx.Tag.Throws(typeof(TimeoutException), "The workflow could not be unloaded within the given timeout.")]
        public void Unload() => this.Unload(ActivityDefaults.SaveTimeout);

        [Fx.Tag.InheritThrows(From = "Unload")]
        public void Unload(TimeSpan timeout)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            var timeoutHelper = new TimeoutHelper(timeout);

            var operation = new RequiresPersistenceOperation();

            try
            {
                this.WaitForTurn(operation, timeoutHelper.RemainingTime());

                this.ValidateStateForUnload();
                if (this.state != WorkflowApplicationState.Unloaded) // Unload on unload is a no-op
                {
                    PersistenceOperation persistenceOperation;

                    if (this.Controller.State == WorkflowInstanceState.Complete)
                    {
                        persistenceOperation = PersistenceOperation.Complete;
                    }
                    else
                    {
                        persistenceOperation = PersistenceOperation.Unload;
                    }

                    this.PersistCore(ref timeoutHelper, persistenceOperation);
                }
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

        [Fx.Tag.InheritThrows(From = "Unload")]
        public IAsyncResult BeginUnload(AsyncCallback callback, object state) => this.BeginUnload(ActivityDefaults.SaveTimeout, callback, state);

        [Fx.Tag.InheritThrows(From = "Unload")]
        public IAsyncResult BeginUnload(TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            return new UnloadOrPersistAsyncResult(this, timeout, PersistenceOperation.Unload, false, false, callback, state);
        }

        [Fx.Tag.InheritThrows(From = "Unload")]
        public void EndUnload(IAsyncResult result) => UnloadOrPersistAsyncResult.End(result);

        private IDictionary<XName, InstanceValue> GetInstanceMetadata()
        {
            if (this.DefinitionIdentity != null)
            {
                if (this.instanceMetadata == null)
                {
                    this.instanceMetadata = new Dictionary<XName, InstanceValue>(2);
                }
                if (!this.instanceMetadata.ContainsKey(WorkflowNamespace.WorkflowHostType))
                {
                    this.instanceMetadata.Add(WorkflowNamespace.WorkflowHostType, new InstanceValue(Workflow45Namespace.WorkflowApplication));
                }
                this.instanceMetadata[Workflow45Namespace.DefinitionIdentity] =
                    new InstanceValue(this.DefinitionIdentity, InstanceValueOptions.Optional);
            }
            return this.instanceMetadata;
        }

        private void UpdateInstanceMetadata() =>
            // Update the metadata to reflect the new identity after a Dynamic Update
            this.persistenceManager.SetMutablemetadata(new Dictionary<XName, InstanceValue>
            {
                { Workflow45Namespace.DefinitionIdentity, new InstanceValue(this.DefinitionIdentity, InstanceValueOptions.Optional) }
            });

        private void ThrowIfMulticast(Delegate value)
        {
            if (value != null && value.GetInvocationList().Length > 1)
            {
                throw FxTrace.Exception.Argument(nameof(value), SR.OnlySingleCastDelegatesAllowed);
            }
        }

        private void ThrowIfAborted()
        {
            if (this.state == WorkflowApplicationState.Aborted)
            {
                throw FxTrace.Exception.AsError(new WorkflowApplicationAbortedException(SR.WorkflowApplicationAborted(this.Id), this.Id));
            }
        }

        private void ThrowIfTerminatedOrCompleted()
        {
            if (this.hasRaisedCompleted)
            {
                this.Controller.GetCompletionState(out var completionException);
                if (completionException != null)
                {
                    throw FxTrace.Exception.AsError(new WorkflowApplicationTerminatedException(SR.WorkflowApplicationTerminated(this.Id), this.Id, completionException));
                }
                else
                {
                    throw FxTrace.Exception.AsError(new WorkflowApplicationCompletedException(SR.WorkflowApplicationCompleted(this.Id), this.Id));
                }
            }
        }

        private void ThrowIfUnloaded()
        {
            if (this.state == WorkflowApplicationState.Unloaded)
            {
                throw FxTrace.Exception.AsError(new WorkflowApplicationUnloadedException(SR.WorkflowApplicationUnloaded(this.Id), this.Id));
            }
        }

        private void ThrowIfNoInstanceStore()
        {
            if (!this.HasPersistenceProvider)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.InstanceStoreRequiredToPersist));
            }
        }

        private void ThrowIfHandlerThread()
        {
            if (this.IsHandlerThread)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotPerformOperationFromHandlerThread));
            }
        }

        private void ValidateStateForRun()
        {
            // WorkflowInstanceException validations
            this.ThrowIfAborted();
            this.ThrowIfTerminatedOrCompleted();
            this.ThrowIfUnloaded();
        }

        private void ValidateStateForGetAllBookmarks()
        {
            // WorkflowInstanceException validations
            this.ThrowIfAborted();
            this.ThrowIfTerminatedOrCompleted();
            this.ThrowIfUnloaded();
        }

        private void ValidateStateForCancel() =>
            // WorkflowInstanceException validations
            this.ThrowIfAborted();// We only validate that we aren't aborted and no-op otherwise. This is because the// scenario for calling cancel is for it to be a best attempt from an unknown thread.// The less it throws the easier it is to author a host.

        private void ValidateStateForLoad()
        {
            this.ThrowIfAborted();
            this.ThrowIfReadOnly(); // only allow a single Load() or Run()
            if (this.instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
            }
        }

        private void ValidateStateForPersist()
        {
            // WorkflowInstanceException validations
            this.ThrowIfAborted();
            this.ThrowIfTerminatedOrCompleted();
            this.ThrowIfUnloaded();

            // Other validations
            this.ThrowIfNoInstanceStore();
        }

        private void ValidateStateForUnload()
        {
            // WorkflowInstanceException validations
            this.ThrowIfAborted();

            // Other validations
            if (this.Controller.State != WorkflowInstanceState.Complete)
            {
                this.ThrowIfNoInstanceStore();
            }
        }

        private void ValidateStateForTerminate()
        {
            // WorkflowInstanceException validations
            this.ThrowIfAborted();
            this.ThrowIfTerminatedOrCompleted();
            this.ThrowIfUnloaded();
        }

        private enum PersistenceOperation : byte
        {
            Complete,
            Save,
            Unload
        }

        private enum WorkflowApplicationState : byte
        {
            Paused,
            Runnable,
            Unloaded,
            Aborted
        }

        internal class SynchronousSynchronizationContext : SynchronizationContext
        {
            private static SynchronousSynchronizationContext value;

            private SynchronousSynchronizationContext()
            {
            }

            public static SynchronousSynchronizationContext Value
            {
                get
                {
                    if (value == null)
                    {
                        value = new SynchronousSynchronizationContext();
                    }
                    return value;
                }
            }

            public override void Post(SendOrPostCallback d, object state) => d(state);

            public override void Send(SendOrPostCallback d, object state) => d(state);
        }

        private class InvokeAsyncResult : AsyncResult
        {
            private static Action<object, TimeoutException> waitCompleteCallback;
            private WorkflowApplication instance;
            private readonly AsyncWaitHandle completionWaiter;
            private IDictionary<string, object> outputs;
            private Exception completionException;

            public InvokeAsyncResult(Activity activity, IDictionary<string, object> inputs, WorkflowInstanceExtensionManager extensions, TimeSpan timeout, SynchronizationContext syncContext, AsyncInvokeContext invokeContext, AsyncCallback callback, object state)
                : base(callback, state)
            {
                Fx.Assert(activity != null, "Need an activity");

                this.completionWaiter = new AsyncWaitHandle();
                syncContext = syncContext ?? SynchronousSynchronizationContext.Value;

                this.instance = WorkflowApplication.StartInvoke(activity, inputs, extensions, syncContext, new Action(this.OnInvokeComplete), invokeContext);

                if (this.completionWaiter.WaitAsync(WaitCompleteCallback, this, timeout))
                {
                    var completeSelf = this.OnWorkflowCompletion();

                    if (completeSelf)
                    {
                        if (this.completionException != null)
                        {
                            throw FxTrace.Exception.AsError(this.completionException);
                        }
                        else
                        {
                            this.Complete(true);
                        }
                    }
                }
            }

            private static Action<object, TimeoutException> WaitCompleteCallback
            {
                get
                {
                    if (waitCompleteCallback == null)
                    {
                        waitCompleteCallback = new Action<object, TimeoutException>(OnWaitComplete);
                    }

                    return waitCompleteCallback;
                }
            }

            public static IDictionary<string, object> End(IAsyncResult result)
            {
                var thisPtr = AsyncResult.End<InvokeAsyncResult>(result);
                return thisPtr.outputs;
            }

            private void OnInvokeComplete() => this.completionWaiter.Set();

            private static void OnWaitComplete(object state, TimeoutException asyncException)
            {
                var thisPtr = (InvokeAsyncResult)state;

                if (asyncException != null)
                {
                    thisPtr.instance.Abort(SR.AbortingDueToInstanceTimeout);
                    thisPtr.Complete(false, asyncException);
                    return;
                }

                var completeSelf = true;

                try
                {
                    completeSelf = thisPtr.OnWorkflowCompletion();
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    thisPtr.completionException = e;
                }

                if (completeSelf)
                {
                    thisPtr.Complete(false, thisPtr.completionException);
                }
            }

            private bool OnWorkflowCompletion()
            {
                if (this.instance.Controller.State == WorkflowInstanceState.Aborted)
                {
                    this.completionException = new WorkflowApplicationAbortedException(SR.DefaultAbortReason, this.instance.Controller.GetAbortReason());
                }
                else
                {
                    Fx.Assert(this.instance.Controller.State == WorkflowInstanceState.Complete, "We should only get here when we are completed.");

                    this.instance.Controller.GetCompletionState(out this.outputs, out this.completionException);
                }

                return true;
            }
        }

        private class ResumeBookmarkAsyncResult : AsyncResult
        {
            private static AsyncCompletion resumedCallback = new AsyncCompletion(OnResumed);
            private static readonly Action<object, TimeoutException> waitCompleteCallback = new Action<object, TimeoutException>(OnWaitComplete);
            private static readonly AsyncCompletion trackingCompleteCallback = new AsyncCompletion(OnTrackingComplete);
            private WorkflowApplication instance;
            private readonly Bookmark bookmark;
            private readonly object value;
            private BookmarkResumptionResult resumptionResult;
            private TimeoutHelper timeoutHelper;
            private readonly bool isFromExtension;
            private bool pendedUnenqueued;
            private InstanceOperation currentOperation;

            public ResumeBookmarkAsyncResult(WorkflowApplication instance, Bookmark bookmark, object value, TimeSpan timeout, AsyncCallback callback, object state)
                : this(instance, bookmark, value, false, timeout, callback, state)
            {
            }

            public ResumeBookmarkAsyncResult(WorkflowApplication instance, Bookmark bookmark, object value, bool isFromExtension, TimeSpan timeout, AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.instance = instance;
                this.bookmark = bookmark;
                this.value = value;
                this.isFromExtension = isFromExtension;
                this.timeoutHelper = new TimeoutHelper(timeout);

                var completeSelf = false;
                var success = false;

                this.OnCompleting = new Action<AsyncResult, Exception>(this.Finally);

                try
                {
                    if (!this.instance.hasCalledRun && !this.isFromExtension)
                    {
                        // Increment the pending unenqueued count so we don't raise idle in the time
                        // between when the Run completes and when we enqueue our InstanceOperation.
                        this.pendedUnenqueued = true;
                        this.instance.IncrementPendingUnenqueud();

                        var result = this.instance.BeginInternalRun(this.timeoutHelper.RemainingTime(), false, this.PrepareAsyncCompletion(resumedCallback), this);
                        if (result.CompletedSynchronously)
                        {
                            completeSelf = OnResumed(result);
                        }
                    }
                    else
                    {
                        completeSelf = this.StartResumptionLoop();
                    }

                    success = true;
                }
                finally
                {
                    // We only want to call this if we are throwing. Otherwise OnCompleting will
                    // take care of it.
                    if (!success)
                    {
                        this.Finally(null, null);
                    }
                }

                if (completeSelf)
                {
                    this.Complete(true);
                }
            }

            public static BookmarkResumptionResult End(IAsyncResult result)
            {
                var thisPtr = AsyncResult.End<ResumeBookmarkAsyncResult>(result);

                return thisPtr.resumptionResult;
            }

            private void ClearPendedUnenqueued()
            {
                if (this.pendedUnenqueued)
                {
                    this.pendedUnenqueued = false;
                    this.instance.DecrementPendingUnenqueud();
                }
            }

            private void NotifyOperationComplete()
            {
                var lastOperation = this.currentOperation;
                this.currentOperation = null;
                this.instance.NotifyOperationComplete(lastOperation);
            }

            private void Finally(AsyncResult result, Exception completionException)
            {
                this.ClearPendedUnenqueued();
                this.NotifyOperationComplete();
            }

            private static bool OnResumed(IAsyncResult result)
            {
                var thisPtr = (ResumeBookmarkAsyncResult)result.AsyncState;
                thisPtr.instance.EndRun(result);
                return thisPtr.StartResumptionLoop();
            }

            private bool StartResumptionLoop()
            {
                this.currentOperation = new RequiresIdleOperation(this.isFromExtension);
                return this.WaitOnCurrentOperation();
            }

            private bool WaitOnCurrentOperation()
            {
                var stillSync = true;
                var tryOneMore = true;

                while (tryOneMore)
                {
                    tryOneMore = false;

                    Fx.Assert(this.currentOperation != null, "We should always have a current operation here.");

                    if (this.instance.WaitForTurnAsync(this.currentOperation, this.timeoutHelper.RemainingTime(), waitCompleteCallback, this))
                    {
                        this.ClearPendedUnenqueued();

                        if (this.CheckIfBookmarksAreInvalid())
                        {
                            stillSync = true;
                        }
                        else
                        {
                            stillSync = this.ProcessResumption();

                            tryOneMore = this.resumptionResult == BookmarkResumptionResult.NotReady;
                        }
                    }
                    else
                    {
                        stillSync = false;
                    }
                }

                return stillSync;
            }

            private static void OnWaitComplete(object state, TimeoutException asyncException)
            {
                var thisPtr = (ResumeBookmarkAsyncResult)state;

                if (asyncException != null)
                {
                    thisPtr.Complete(false, asyncException);
                    return;
                }

                Exception completionException = null;
                var completeSelf = false;

                try
                {
                    thisPtr.ClearPendedUnenqueued();

                    if (thisPtr.CheckIfBookmarksAreInvalid())
                    {
                        completeSelf = true;
                    }
                    else
                    {
                        completeSelf = thisPtr.ProcessResumption();

                        if (thisPtr.resumptionResult == BookmarkResumptionResult.NotReady)
                        {
                            completeSelf = thisPtr.WaitOnCurrentOperation();
                        }
                    }
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    completeSelf = true;
                    completionException = e;
                }

                if (completeSelf)
                {
                    thisPtr.Complete(false, completionException);
                }
            }

            private bool CheckIfBookmarksAreInvalid()
            {
                if (this.instance.AreBookmarksInvalid(out this.resumptionResult))
                {
                    return true;
                }

                return false;
            }

            private bool ProcessResumption()
            {
                var stillSync = true;

                this.resumptionResult = this.instance.ResumeBookmarkCore(this.bookmark, this.value);

                if (this.resumptionResult == BookmarkResumptionResult.Success)
                {
                    if (this.instance.Controller.HasPendingTrackingRecords)
                    {
                        var result = this.instance.Controller.BeginFlushTrackingRecords(this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(trackingCompleteCallback), this);

                        if (result.CompletedSynchronously)
                        {
                            stillSync = OnTrackingComplete(result);
                        }
                        else
                        {
                            stillSync = false;
                        }
                    }
                }
                else if (this.resumptionResult == BookmarkResumptionResult.NotReady)
                {
                    this.NotifyOperationComplete();
                    this.currentOperation = new DeferredRequiresIdleOperation();
                }

                return stillSync;
            }

            private static bool OnTrackingComplete(IAsyncResult result)
            {
                var thisPtr = (ResumeBookmarkAsyncResult)result.AsyncState;

                thisPtr.instance.Controller.EndFlushTrackingRecords(result);

                return true;
            }
        }

        private class UnloadOrPersistAsyncResult : TransactedAsyncResult
        {
            private static Action<object, TimeoutException> waitCompleteCallback = new Action<object, TimeoutException>(OnWaitComplete);
            private static readonly AsyncCompletion savedCallback = new AsyncCompletion(OnSaved);
            private static readonly AsyncCompletion persistedCallback = new AsyncCompletion(OnPersisted);
            private static AsyncCompletion initializedCallback = new AsyncCompletion(OnProviderInitialized);
            private static AsyncCompletion readynessEnsuredCallback = new AsyncCompletion(OnProviderReadynessEnsured);
            private static readonly AsyncCompletion trackingCompleteCallback = new AsyncCompletion(OnTrackingComplete);
            private static AsyncCompletion deleteOwnerCompleteCallback = new AsyncCompletion(OnOwnerDeleted);
            private static readonly AsyncCompletion completeContextCallback = new AsyncCompletion(OnCompleteContext);
            private static Action<AsyncResult, Exception> completeCallback = new Action<AsyncResult, Exception>(OnComplete);
            private DependentTransaction dependentTransaction;
            private WorkflowApplication instance;
            private readonly bool isUnloaded;
            private TimeoutHelper timeoutHelper;
            private PersistenceOperation operation;
            private RequiresPersistenceOperation instanceOperation;
            private WorkflowPersistenceContext context;
            private IDictionary<XName, InstanceValue> data;
            private PersistencePipeline pipeline;
            private readonly bool isInternalPersist;

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

            private static bool OnProviderInitialized(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                thisPtr.instance.persistenceManager.EndInitialize(result);
                return thisPtr.EnsureProviderReadyness();
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

            private static bool OnProviderReadynessEnsured(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                thisPtr.instance.persistenceManager.EndEnsureReadyness(result);
                return thisPtr.Track();
            }

            public static void End(IAsyncResult result) => AsyncResult.End<UnloadOrPersistAsyncResult>(result);

            private void NotifyOperationComplete()
            {
                var localInstanceOperation = this.instanceOperation;
                this.instanceOperation = null;
                this.instance.NotifyOperationComplete(localInstanceOperation);
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

            private static bool OnTrackingComplete(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                thisPtr.instance.Controller.EndFlushTrackingRecords(result);
                return thisPtr.CollectAndMap();
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

            private static bool OnCompleteContext(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                thisPtr.context.EndComplete(result);

                return thisPtr.DeleteOwner();
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

            private static bool OnOwnerDeleted(IAsyncResult result)
            {
                var thisPtr = (UnloadOrPersistAsyncResult)result.AsyncState;
                thisPtr.instance.persistenceManager.EndDeleteOwner(result);
                return thisPtr.CloseInstance();
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
        }

        private abstract class SimpleOperationAsyncResult : AsyncResult
        {
            private static readonly Action<object, TimeoutException> waitCompleteCallback = new Action<object, TimeoutException>(OnWaitComplete);
            private static readonly AsyncCallback trackingCompleteCallback = Fx.ThunkCallback(new AsyncCallback(OnTrackingComplete));
            private WorkflowApplication instance;
            private TimeoutHelper timeoutHelper;

            protected SimpleOperationAsyncResult(WorkflowApplication instance, AsyncCallback callback, object state)
                : base(callback, state) => this.instance = instance;

            protected WorkflowApplication Instance => this.instance;

            protected void Run(TimeSpan timeout)
            {
                this.timeoutHelper = new TimeoutHelper(timeout);

                var operation = new InstanceOperation();

                var completeSelf = true;

                try
                {
                    completeSelf = this.instance.WaitForTurnAsync(operation, this.timeoutHelper.RemainingTime(), waitCompleteCallback, this);

                    if (completeSelf)
                    {
                        this.ValidateState();

                        completeSelf = this.PerformOperationAndTrack();
                    }
                }
                finally
                {
                    if (completeSelf)
                    {
                        this.instance.NotifyOperationComplete(operation);
                    }
                }

                if (completeSelf)
                {
                    this.Complete(true);
                }
            }

            private bool PerformOperationAndTrack()
            {
                this.PerformOperation();

                var completedSync = true;

                if (this.instance.Controller.HasPendingTrackingRecords)
                {
                    var trackingResult = this.instance.Controller.BeginFlushTrackingRecords(this.timeoutHelper.RemainingTime(), trackingCompleteCallback, this);

                    if (trackingResult.CompletedSynchronously)
                    {
                        this.instance.Controller.EndFlushTrackingRecords(trackingResult);
                    }
                    else
                    {
                        completedSync = false;
                    }
                }

                return completedSync;
            }

            private static void OnWaitComplete(object state, TimeoutException asyncException)
            {
                var thisPtr = (SimpleOperationAsyncResult)state;

                if (asyncException != null)
                {
                    thisPtr.Complete(false, asyncException);
                }
                else
                {
                    Exception completionException = null;
                    var completeSelf = true;

                    try
                    {
                        thisPtr.ValidateState();

                        completeSelf = thisPtr.PerformOperationAndTrack();
                    }
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                        {
                            throw;
                        }

                        completionException = e;
                    }
                    finally
                    {
                        if (completeSelf)
                        {
                            thisPtr.instance.ForceNotifyOperationComplete();
                        }
                    }

                    if (completeSelf)
                    {
                        thisPtr.Complete(false, completionException);
                    }
                }
            }

            private static void OnTrackingComplete(IAsyncResult result)
            {
                if (result.CompletedSynchronously)
                {
                    return;
                }

                var thisPtr = (SimpleOperationAsyncResult)result.AsyncState;

                Exception completionException = null;

                try
                {
                    thisPtr.instance.Controller.EndFlushTrackingRecords(result);
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    completionException = e;
                }
                finally
                {
                    thisPtr.instance.ForceNotifyOperationComplete();
                }

                thisPtr.Complete(false, completionException);
            }

            protected abstract void ValidateState();

            protected abstract void PerformOperation();
        }

        private class TerminateAsyncResult : SimpleOperationAsyncResult
        {
            private readonly Exception reason;

            private TerminateAsyncResult(WorkflowApplication instance, Exception reason, AsyncCallback callback, object state)
                : base(instance, callback, state) => this.reason = reason;

            public static TerminateAsyncResult Create(WorkflowApplication instance, Exception reason, TimeSpan timeout, AsyncCallback callback, object state)
            {
                var result = new TerminateAsyncResult(instance, reason, callback, state);
                result.Run(timeout);
                return result;
            }

            public static void End(IAsyncResult result) => AsyncResult.End<TerminateAsyncResult>(result);

            protected override void ValidateState() => this.Instance.ValidateStateForTerminate();

            protected override void PerformOperation() => this.Instance.TerminateCore(this.reason);
        }

        private class CancelAsyncResult : SimpleOperationAsyncResult
        {
            private CancelAsyncResult(WorkflowApplication instance, AsyncCallback callback, object state)
                : base(instance, callback, state)
            {
            }

            public static CancelAsyncResult Create(WorkflowApplication instance, TimeSpan timeout, AsyncCallback callback, object state)
            {
                var result = new CancelAsyncResult(instance, callback, state);
                result.Run(timeout);
                return result;
            }

            public static void End(IAsyncResult result) => AsyncResult.End<CancelAsyncResult>(result);

            protected override void ValidateState() => this.Instance.ValidateStateForCancel();

            protected override void PerformOperation() => this.Instance.CancelCore();
        }

        private class RunAsyncResult : SimpleOperationAsyncResult
        {
            private readonly bool isUserRun;

            private RunAsyncResult(WorkflowApplication instance, bool isUserRun, AsyncCallback callback, object state)
                : base(instance, callback, state) => this.isUserRun = isUserRun;

            public static RunAsyncResult Create(WorkflowApplication instance, bool isUserRun, TimeSpan timeout, AsyncCallback callback, object state)
            {
                var result = new RunAsyncResult(instance, isUserRun, callback, state);
                result.Run(timeout);
                return result;
            }

            public static void End(IAsyncResult result) => AsyncResult.End<RunAsyncResult>(result);

            protected override void ValidateState() => this.Instance.ValidateStateForRun();

            protected override void PerformOperation()
            {
                if (this.isUserRun)
                {
                    // We set this to true here so that idle will be raised regardless of whether
                    // any work is performed.
                    this.Instance.hasExecutionOccurredSinceLastIdle = true;
                }

                this.Instance.RunCore();
            }
        }

        private class UnlockInstanceAsyncResult : TransactedAsyncResult
        {
            private static AsyncCompletion instanceUnlockedCallback = new AsyncCompletion(OnInstanceUnlocked);
            private static AsyncCompletion ownerDeletedCallback = new AsyncCompletion(OnOwnerDeleted);
            private static Action<AsyncResult, Exception> completeCallback = new Action<AsyncResult, Exception>(OnComplete);
            private readonly PersistenceManager persistenceManager;
            private readonly TimeoutHelper timeoutHelper;
            private DependentTransaction dependentTransaction;

            public UnlockInstanceAsyncResult(PersistenceManager persistenceManager, TimeoutHelper timeoutHelper, AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.persistenceManager = persistenceManager;
                this.timeoutHelper = timeoutHelper;

                var currentTransaction = Transaction.Current;
                if (currentTransaction != null)
                {
                    this.dependentTransaction = currentTransaction.DependentClone(DependentCloneOption.BlockCommitUntilComplete);
                }

                this.OnCompleting = UnlockInstanceAsyncResult.completeCallback;

                var success = false;
                try
                {
                    IAsyncResult result;
                    using (this.PrepareTransactionalCall(this.dependentTransaction))
                    {
                        if (this.persistenceManager.OwnerWasCreated)
                        {
                            // if the owner was created by this WorkflowApplication, delete it. This
                            // implicitly unlocks the instance.
                            result = this.persistenceManager.BeginDeleteOwner(this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(ownerDeletedCallback), this);
                        }
                        else
                        {
                            result = this.persistenceManager.BeginUnlock(this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(instanceUnlockedCallback), this);
                        }
                    }

                    if (this.SyncContinue(result))
                    {
                        this.Complete(true);
                    }

                    success = true;
                }
                finally
                {
                    if (!success)
                    {
                        this.persistenceManager.Abort();
                    }
                }
            }

            public static void End(IAsyncResult result) => AsyncResult.End<UnlockInstanceAsyncResult>(result);

            private static bool OnInstanceUnlocked(IAsyncResult result)
            {
                var thisPtr = (UnlockInstanceAsyncResult)result.AsyncState;
                thisPtr.persistenceManager.EndUnlock(result);
                return true;
            }

            private static bool OnOwnerDeleted(IAsyncResult result)
            {
                var thisPtr = (UnlockInstanceAsyncResult)result.AsyncState;
                thisPtr.persistenceManager.EndDeleteOwner(result);
                return true;
            }

            private static void OnComplete(AsyncResult result, Exception exception)
            {
                var thisPtr = (UnlockInstanceAsyncResult)result;
                if (thisPtr.dependentTransaction != null)
                {
                    thisPtr.dependentTransaction.Complete();
                }
                thisPtr.persistenceManager.Abort();
            }
        }

        private class LoadAsyncResult : TransactedAsyncResult
        {
            private static readonly Action<object, TimeoutException> waitCompleteCallback = new Action<object, TimeoutException>(OnWaitComplete);
            private static readonly AsyncCompletion providerRegisteredCallback = new AsyncCompletion(OnProviderRegistered);
            private static readonly AsyncCompletion loadCompleteCallback = new AsyncCompletion(OnLoadComplete);
            private static readonly AsyncCompletion loadPipelineCallback = new AsyncCompletion(OnLoadPipeline);
            private static readonly AsyncCompletion completeContextCallback = new AsyncCompletion(OnCompleteContext);
            private static Action<AsyncResult, Exception> completeCallback = new Action<AsyncResult, Exception>(OnComplete);
            private readonly WorkflowApplication application;
            private readonly PersistenceManager persistenceManager;
            private readonly TimeoutHelper timeoutHelper;
            private readonly bool loadAny;
            private object deserializedRuntimeState;
            private PersistencePipeline pipeline;
            private WorkflowPersistenceContext context;
            private DependentTransaction dependentTransaction;
            private IDictionary<XName, InstanceValue> values;
#if NET45
            DynamicUpdateMap updateMap;
#endif
            private InstanceOperation instanceOperation;

            public LoadAsyncResult(WorkflowApplication application, PersistenceManager persistenceManager,
                IDictionary<XName, InstanceValue> values,
#if NET45
                        DynamicUpdateMap updateMap,

#endif
                TimeSpan timeout,
                AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.application = application;
                this.persistenceManager = persistenceManager;
                this.values = values;
                this.timeoutHelper = new TimeoutHelper(timeout);
#if NET45
                this.updateMap = updateMap;
#endif

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
#if NET45
                catch (InstanceUpdateException e)
                {
                    updateException = e;
                    throw;
                }
#endif
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

            public static void End(IAsyncResult result) => AsyncResult.End<LoadAsyncResult>(result);

            public static WorkflowApplicationInstance EndAndCreateInstance(IAsyncResult result)
            {
                var thisPtr = AsyncResult.End<LoadAsyncResult>(result);
                Fx.AssertAndThrow(thisPtr.application == null, "Should not create a WorkflowApplicationInstance if we already have a WorkflowApplication");

                var deserializedRuntimeState = WorkflowApplication.ExtractRuntimeState(thisPtr.values, thisPtr.persistenceManager.InstanceId);
                return new WorkflowApplicationInstance(thisPtr.persistenceManager, thisPtr.values, deserializedRuntimeState.WorkflowIdentity);
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

            private static bool OnProviderRegistered(IAsyncResult result)
            {
                var thisPtr = (LoadAsyncResult)result.AsyncState;
                thisPtr.persistenceManager.EndInitialize(result);
                return thisPtr.Load();
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

            private static bool OnLoadComplete(IAsyncResult result)
            {
                var thisPtr = (LoadAsyncResult)result.AsyncState;
                return thisPtr.LoadValues(result);
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

            private bool CompleteContext()
            {
                if (this.application != null)
                {
#if NET45
                    this.application.Initialize(this.deserializedRuntimeState, this.updateMap);
                    if (this.updateMap != null)
                    {
                        this.application.UpdateInstanceMetadata();
                    }
#else
                    this.application.Initialize(this.deserializedRuntimeState);
#endif
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

            private static bool OnCompleteContext(IAsyncResult result)
            {
                var thisPtr = (LoadAsyncResult)result.AsyncState;
                thisPtr.context.EndComplete(result);
                return thisPtr.Finish();
            }

            private bool Finish()
            {
                if (this.pipeline != null)
                {
                    this.pipeline.Publish();
                }
                return true;
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
        }

        // this class is not a general purpose SyncContext and is only meant to work for workflow
        // scenarios, where the scheduler ensures at most one work item pending. The scheduler
        // ensures that Invoke must run before Post is called on a different thread.
        private class PumpBasedSynchronizationContext : SynchronizationContext
        {
            // The waitObject is cached per thread so that we can avoid the cost of creating events
            // for multiple synchronous invokes.
            [ThreadStatic]
            private static AutoResetEvent waitObject;

            private AutoResetEvent queueWaiter;
            private WorkItem currentWorkItem;
            private readonly object thisLock;
            private TimeoutHelper timeoutHelper;

            public PumpBasedSynchronizationContext(TimeSpan timeout)
            {
                this.timeoutHelper = new TimeoutHelper(timeout);
                this.thisLock = new object();
            }

            private bool IsInvokeCompleted
            {
                get;
                set;
            }

            public void DoPump()
            {
                Fx.Assert(this.currentWorkItem != null, "the work item cannot be null");
                WorkItem workItem;

                lock (this.thisLock)
                {
                    if (PumpBasedSynchronizationContext.waitObject == null)
                    {
                        PumpBasedSynchronizationContext.waitObject = new AutoResetEvent(false);
                    }
                    this.queueWaiter = PumpBasedSynchronizationContext.waitObject;

                    workItem = this.currentWorkItem;
                    this.currentWorkItem = null;
                    workItem.Invoke();
                }

                Fx.Assert(this.queueWaiter != null, "queue waiter cannot be null");

                while (this.WaitForNextItem())
                {
                    Fx.Assert(this.currentWorkItem != null, "the work item cannot be null");
                    workItem = this.currentWorkItem;
                    this.currentWorkItem = null;
                    workItem.Invoke();
                }
            }

            public override void Post(SendOrPostCallback d, object state) => this.ScheduleWorkItem(new WorkItem(d, state));

            public override void Send(SendOrPostCallback d, object state) => throw FxTrace.Exception.AsError(new NotSupportedException(SR.SendNotSupported));

            // Since tracking can go async this may or may not be called directly under a call to
            // workItem.Invoke. Also, the scheduler may call OnNotifyPaused or
            // OnNotifyUnhandledException from any random thread if runtime goes async (post-work
            // item tracking, AsyncCodeActivity).
            public void OnInvokeCompleted()
            {
                Fx.AssertAndFailFast(this.currentWorkItem == null, "There can be no pending work items when complete");

                this.IsInvokeCompleted = true;

                lock (this.thisLock)
                {
                    if (this.queueWaiter != null)
                    {
                        // Since we don't know which thread this is being called from we just set
                        // the waiter directly rather than doing our SetWaiter cleanup.
                        this.queueWaiter.Set();
                    }
                }
            }

            private void ScheduleWorkItem(WorkItem item)
            {
                lock (this.thisLock)
                {
                    Fx.AssertAndFailFast(this.currentWorkItem == null, "There cannot be more than 1 work item at a given time");
                    this.currentWorkItem = item;
                    if (this.queueWaiter != null)
                    {
                        // Since we don't know which thread this is being called from we just set
                        // the waiter directly rather than doing our SetWaiter cleanup.
                        this.queueWaiter.Set();
                    }
                }
            }

            private bool WaitOne(AutoResetEvent waiter, TimeSpan timeout)
            {
                var success = false;
                try
                {
                    var result = TimeoutHelper.WaitOne(waiter, timeout);
                    // if the wait timed out, reset the thread static
                    success = result;
                    return result;
                }
                finally
                {
                    if (!success)
                    {
                        PumpBasedSynchronizationContext.waitObject = null;
                    }
                }
            }

            private bool WaitForNextItem()
            {
                if (!this.WaitOne(this.queueWaiter, this.timeoutHelper.RemainingTime()))
                {
                    throw FxTrace.Exception.AsError(new TimeoutException(SR.TimeoutOnOperation(this.timeoutHelper.OriginalTimeout)));
                }

                // We need to check this after the wait as well in case the notification came in asynchronously
                if (this.IsInvokeCompleted)
                {
                    return false;
                }

                return true;
            }

            private class WorkItem
            {
                private readonly SendOrPostCallback callback;
                private readonly object state;

                public WorkItem(SendOrPostCallback callback, object state)
                {
                    this.callback = callback;
                    this.state = state;
                }

                public void Invoke() => this.callback(this.state);
            }
        }

        private class WorkflowEventData
        {
            public WorkflowEventData(WorkflowApplication instance) => this.Instance = instance;

            public WorkflowApplication Instance
            {
                get;
                private set;
            }

            public Func<IAsyncResult, WorkflowApplication, bool, bool> NextCallback
            {
                get;
                set;
            }

            public Exception UnhandledException
            {
                get;
                set;
            }

            public Activity UnhandledExceptionSource
            {
                get;
                set;
            }

            public string UnhandledExceptionSourceInstance
            {
                get;
                set;
            }
        }

        private class IdleEventHandler
        {
            private Func<IAsyncResult, WorkflowApplication, bool, bool> stage1Callback;
            private Func<IAsyncResult, WorkflowApplication, bool, bool> stage2Callback;

            public IdleEventHandler()
            {
            }

            private Func<IAsyncResult, WorkflowApplication, bool, bool> Stage1Callback
            {
                get
                {
                    if (this.stage1Callback == null)
                    {
                        this.stage1Callback = new Func<IAsyncResult, WorkflowApplication, bool, bool>(this.OnStage1Complete);
                    }

                    return this.stage1Callback;
                }
            }

            private Func<IAsyncResult, WorkflowApplication, bool, bool> Stage2Callback
            {
                get
                {
                    if (this.stage2Callback == null)
                    {
                        this.stage2Callback = new Func<IAsyncResult, WorkflowApplication, bool, bool>(this.OnStage2Complete);
                    }

                    return this.stage2Callback;
                }
            }

            public bool Run(WorkflowApplication instance)
            {
                IAsyncResult result = null;

                if (instance.Controller.TrackingEnabled)
                {
                    instance.Controller.Track(new WorkflowInstanceRecord(instance.Id, instance.WorkflowDefinition.DisplayName, WorkflowInstanceStates.Idle, instance.DefinitionIdentity));

                    instance.EventData.NextCallback = this.Stage1Callback;
                    result = instance.Controller.BeginFlushTrackingRecords(ActivityDefaults.TrackingTimeout, EventFrameCallback, instance.EventData);

                    if (!result.CompletedSynchronously)
                    {
                        return false;
                    }
                }

                return this.OnStage1Complete(result, instance, true);
            }

            private bool OnStage1Complete(IAsyncResult lastResult, WorkflowApplication application, bool isStillSync)
            {
                if (lastResult != null)
                {
                    application.Controller.EndFlushTrackingRecords(lastResult);
                }

                IAsyncResult result = null;

                if (application.RaiseIdleEvent())
                {
                    if (application.Controller.IsPersistable && application.persistenceManager != null)
                    {
                        var persistableIdleHandler = application.PersistableIdle;

                        if (persistableIdleHandler != null)
                        {
                            var action = PersistableIdleAction.None;

                            application.handlerThreadId = Thread.CurrentThread.ManagedThreadId;

                            try
                            {
                                application.isInHandler = true;
                                action = persistableIdleHandler(new WorkflowApplicationIdleEventArgs(application));
                            }
                            finally
                            {
                                application.isInHandler = false;
                            }

                            if (TD.WorkflowApplicationPersistableIdleIsEnabled())
                            {
                                TD.WorkflowApplicationPersistableIdle(application.Id.ToString(), action.ToString());
                            }

                            if (action != PersistableIdleAction.None)
                            {
                                var operation = PersistenceOperation.Unload;

                                if (action == PersistableIdleAction.Persist)
                                {
                                    operation = PersistenceOperation.Save;
                                }
                                else if (action != PersistableIdleAction.Unload)
                                {
                                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.InvalidIdleAction));
                                }

                                application.EventData.NextCallback = this.Stage2Callback;
                                result = application.BeginInternalPersist(operation, ActivityDefaults.InternalSaveTimeout, true, EventFrameCallback, application.EventData);

                                if (!result.CompletedSynchronously)
                                {
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            // Trace the default action
                            if (TD.WorkflowApplicationPersistableIdleIsEnabled())
                            {
                                TD.WorkflowApplicationPersistableIdle(application.Id.ToString(), PersistableIdleAction.None.ToString());
                            }
                        }
                    }
                }

                return this.OnStage2Complete(result, application, isStillSync);
            }

            private bool OnStage2Complete(IAsyncResult lastResult, WorkflowApplication instance, bool isStillSync)
            {
                if (lastResult != null)
                {
                    instance.EndInternalPersist(lastResult);
                }

                return true;
            }
        }

        private class CompletedEventHandler
        {
            private Func<IAsyncResult, WorkflowApplication, bool, bool> stage1Callback;
            private Func<IAsyncResult, WorkflowApplication, bool, bool> stage2Callback;

            public CompletedEventHandler()
            {
            }

            private Func<IAsyncResult, WorkflowApplication, bool, bool> Stage1Callback
            {
                get
                {
                    if (this.stage1Callback == null)
                    {
                        this.stage1Callback = new Func<IAsyncResult, WorkflowApplication, bool, bool>(this.OnStage1Complete);
                    }

                    return this.stage1Callback;
                }
            }

            private Func<IAsyncResult, WorkflowApplication, bool, bool> Stage2Callback
            {
                get
                {
                    if (this.stage2Callback == null)
                    {
                        this.stage2Callback = new Func<IAsyncResult, WorkflowApplication, bool, bool>(this.OnStage2Complete);
                    }

                    return this.stage2Callback;
                }
            }

            public bool Run(WorkflowApplication instance)
            {
                IAsyncResult result = null;
                if (instance.Controller.HasPendingTrackingRecords)
                {
                    instance.EventData.NextCallback = this.Stage1Callback;
                    result = instance.Controller.BeginFlushTrackingRecords(ActivityDefaults.TrackingTimeout, EventFrameCallback, instance.EventData);

                    if (!result.CompletedSynchronously)
                    {
                        return false;
                    }
                }

                return this.OnStage1Complete(result, instance, true);
            }

            private bool OnStage1Complete(IAsyncResult lastResult, WorkflowApplication instance, bool isStillSync)
            {
                if (lastResult != null)
                {
                    instance.Controller.EndFlushTrackingRecords(lastResult);
                }
                var completionState = instance.Controller.GetCompletionState(out var outputs, out var completionException);

                if (instance.invokeCompletedCallback == null)
                {
                    var handler = instance.Completed;

                    if (handler != null)
                    {
                        instance.handlerThreadId = Thread.CurrentThread.ManagedThreadId;

                        try
                        {
                            instance.isInHandler = true;
                            handler(new WorkflowApplicationCompletedEventArgs(instance, completionException, completionState, outputs));
                        }
                        finally
                        {
                            instance.isInHandler = false;
                        }
                    }
                }

                switch (completionState)
                {
                    case ActivityInstanceState.Closed:
                        if (TD.WorkflowApplicationCompletedIsEnabled())
                        {
                            TD.WorkflowApplicationCompleted(instance.Id.ToString());
                        }
                        break;

                    case ActivityInstanceState.Canceled:
                        if (TD.WorkflowInstanceCanceledIsEnabled())
                        {
                            TD.WorkflowInstanceCanceled(instance.Id.ToString());
                        }
                        break;

                    case ActivityInstanceState.Faulted:
                        if (TD.WorkflowApplicationTerminatedIsEnabled())
                        {
                            TD.WorkflowApplicationTerminated(instance.Id.ToString(), completionException);
                        }
                        break;
                }

                IAsyncResult result = null;
                Fx.Assert(instance.Controller.IsPersistable, "Should not be in a No Persist Zone once the instance is complete.");
                if (instance.persistenceManager != null || instance.HasPersistenceModule)
                {
                    instance.EventData.NextCallback = this.Stage2Callback;
                    result = instance.BeginInternalPersist(PersistenceOperation.Unload, ActivityDefaults.InternalSaveTimeout, true, EventFrameCallback, instance.EventData);

                    if (!result.CompletedSynchronously)
                    {
                        return false;
                    }
                }
                else
                {
                    instance.MarkUnloaded();
                }

                return this.OnStage2Complete(result, instance, isStillSync);
            }

            private bool OnStage2Complete(IAsyncResult lastResult, WorkflowApplication instance, bool isStillSync)
            {
                if (lastResult != null)
                {
                    instance.EndInternalPersist(lastResult);
                }

                if (instance.invokeCompletedCallback != null)
                {
                    instance.invokeCompletedCallback();
                }

                return true;
            }
        }

        private class UnhandledExceptionEventHandler
        {
            private Func<IAsyncResult, WorkflowApplication, bool, bool> stage1Callback;

            public UnhandledExceptionEventHandler()
            {
            }

            private Func<IAsyncResult, WorkflowApplication, bool, bool> Stage1Callback
            {
                get
                {
                    if (this.stage1Callback == null)
                    {
                        this.stage1Callback = new Func<IAsyncResult, WorkflowApplication, bool, bool>(this.OnStage1Complete);
                    }

                    return this.stage1Callback;
                }
            }

            public bool Run(WorkflowApplication instance, Exception exception, Activity exceptionSource, string exceptionSourceInstanceId)
            {
                IAsyncResult result = null;

                if (instance.Controller.HasPendingTrackingRecords)
                {
                    instance.EventData.NextCallback = this.Stage1Callback;
                    instance.EventData.UnhandledException = exception;
                    instance.EventData.UnhandledExceptionSource = exceptionSource;
                    instance.EventData.UnhandledExceptionSourceInstance = exceptionSourceInstanceId;
                    result = instance.Controller.BeginFlushTrackingRecords(ActivityDefaults.TrackingTimeout, EventFrameCallback, instance.EventData);

                    if (!result.CompletedSynchronously)
                    {
                        return false;
                    }
                }

                return this.OnStage1Complete(result, instance, exception, exceptionSource, exceptionSourceInstanceId);
            }

            private bool OnStage1Complete(IAsyncResult lastResult, WorkflowApplication instance, bool isStillSync) => this.OnStage1Complete(lastResult, instance, instance.EventData.UnhandledException, instance.EventData.UnhandledExceptionSource, instance.EventData.UnhandledExceptionSourceInstance);

            private bool OnStage1Complete(IAsyncResult lastResult, WorkflowApplication instance, Exception exception, Activity source, string sourceInstanceId)
            {
                if (lastResult != null)
                {
                    instance.Controller.EndFlushTrackingRecords(lastResult);
                }

                var handler = instance.OnUnhandledException;

                var action = UnhandledExceptionAction.Terminate;

                if (handler != null)
                {
                    try
                    {
                        instance.isInHandler = true;
                        instance.handlerThreadId = Thread.CurrentThread.ManagedThreadId;

                        action = handler(new WorkflowApplicationUnhandledExceptionEventArgs(instance, exception, source, sourceInstanceId));
                    }
                    finally
                    {
                        instance.isInHandler = false;
                    }
                }

                if (instance.invokeCompletedCallback != null)
                {
                    action = UnhandledExceptionAction.Terminate;
                }

                if (TD.WorkflowApplicationUnhandledExceptionIsEnabled())
                {
                    TD.WorkflowApplicationUnhandledException(instance.Id.ToString(), source.GetType().ToString(), source.DisplayName, action.ToString(), exception);
                }

                switch (action)
                {
                    case UnhandledExceptionAction.Abort:
                        instance.AbortInstance(exception, true);
                        break;

                    case UnhandledExceptionAction.Cancel:
                        instance.Controller.ScheduleCancel();
                        break;

                    case UnhandledExceptionAction.Terminate:
                        instance.TerminateCore(exception);
                        break;

                    default:
                        throw FxTrace.Exception.AsError(new InvalidOperationException(SR.InvalidUnhandledExceptionAction));
                }

                return true;
            }
        }

        private class InstanceCommandWithTemporaryHandleAsyncResult : TransactedAsyncResult
        {
            private static AsyncCompletion commandCompletedCallback = new AsyncCompletion(OnCommandCompleted);
            private static Action<AsyncResult, Exception> completeCallback = new Action<AsyncResult, Exception>(OnComplete);
            private readonly InstancePersistenceCommand command;
            private DependentTransaction dependentTransaction;
            private InstanceStore instanceStore;
            private InstanceHandle temporaryHandle;
            private InstanceView commandResult;

            public InstanceCommandWithTemporaryHandleAsyncResult(InstanceStore instanceStore, InstancePersistenceCommand command,
                TimeSpan timeout, AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.instanceStore = instanceStore;
                this.command = command;
                this.temporaryHandle = instanceStore.CreateInstanceHandle();

                var currentTransaction = Transaction.Current;
                if (currentTransaction != null)
                {
                    this.dependentTransaction = currentTransaction.DependentClone(DependentCloneOption.BlockCommitUntilComplete);
                }

                this.OnCompleting = completeCallback;

                IAsyncResult result;
                using (this.PrepareTransactionalCall(this.dependentTransaction))
                {
                    result = instanceStore.BeginExecute(this.temporaryHandle, command, timeout, this.PrepareAsyncCompletion(commandCompletedCallback), this);
                }

                if (this.SyncContinue(result))
                {
                    this.Complete(true);
                }
            }

            public static void End(IAsyncResult result, out InstanceStore instanceStore, out InstanceView commandResult)
            {
                var thisPtr = AsyncResult.End<InstanceCommandWithTemporaryHandleAsyncResult>(result);
                instanceStore = thisPtr.instanceStore;
                commandResult = thisPtr.commandResult;
            }

            private static bool OnCommandCompleted(IAsyncResult result)
            {
                var thisPtr = (InstanceCommandWithTemporaryHandleAsyncResult)result.AsyncState;
                thisPtr.commandResult = thisPtr.instanceStore.EndExecute(result);
                return true;
            }

            private static void OnComplete(AsyncResult result, Exception exception)
            {
                var thisPtr = (InstanceCommandWithTemporaryHandleAsyncResult)result;
                if (thisPtr.dependentTransaction != null)
                {
                    thisPtr.dependentTransaction.Complete();
                }
                thisPtr.temporaryHandle.Free();
            }
        }

        private class InstanceOperation
        {
            private AsyncWaitHandle waitHandle;

            public InstanceOperation()
            {
                this.InterruptsScheduler = true;
                this.RequiresInitialized = true;
            }

            public bool Notified
            {
                get;
                set;
            }

            public int ActionId
            {
                get;
                set;
            }

            public bool InterruptsScheduler
            {
                get;
                protected set;
            }

            public bool RequiresInitialized
            {
                get;
                set;
            }

            public void OnEnqueued() => this.waitHandle = new AsyncWaitHandle();

            public virtual bool CanRun(WorkflowApplication instance) => true;

            public void NotifyTurn()
            {
                Fx.Assert(this.waitHandle != null, "We must have a wait handle.");

                this.waitHandle.Set();
            }

            public bool WaitForTurn(TimeSpan timeout)
            {
                if (this.waitHandle != null)
                {
                    return this.waitHandle.Wait(timeout);
                }

                return true;
            }

            public bool WaitForTurnAsync(TimeSpan timeout, Action<object, TimeoutException> callback, object state)
            {
                if (this.waitHandle != null)
                {
                    return this.waitHandle.WaitAsync(callback, state, timeout);
                }

                return true;
            }
        }

        private class RequiresIdleOperation : InstanceOperation
        {
            private readonly bool requiresRunnableInstance;

            public RequiresIdleOperation()
                : this(false)
            {
            }

            public RequiresIdleOperation(bool requiresRunnableInstance)
            {
                this.InterruptsScheduler = false;
                this.requiresRunnableInstance = requiresRunnableInstance;
            }

            public override bool CanRun(WorkflowApplication instance)
            {
                if (this.requiresRunnableInstance && instance.state != WorkflowApplicationState.Runnable)
                {
                    return false;
                }

                return instance.Controller.State == WorkflowInstanceState.Idle || instance.Controller.State == WorkflowInstanceState.Complete;
            }
        }

        private class DeferredRequiresIdleOperation : InstanceOperation
        {
            public DeferredRequiresIdleOperation() => this.InterruptsScheduler = false;

            public override bool CanRun(WorkflowApplication instance) => (this.ActionId != instance.actionCount && instance.Controller.State == WorkflowInstanceState.Idle) || instance.Controller.State == WorkflowInstanceState.Complete;
        }

        private class RequiresPersistenceOperation : InstanceOperation
        {
            public override bool CanRun(WorkflowApplication instance)
            {
                if (!instance.Controller.IsPersistable && instance.Controller.State != WorkflowInstanceState.Complete)
                {
                    instance.Controller.PauseWhenPersistable();
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        private class WaitForTurnData
        {
            public WaitForTurnData(Action<object, TimeoutException> callback, object state, InstanceOperation operation, WorkflowApplication instance)
            {
                this.Callback = callback;
                this.State = state;
                this.Operation = operation;
                this.Instance = instance;
            }

            public Action<object, TimeoutException> Callback
            {
                get;
                private set;
            }

            public object State
            {
                get;
                private set;
            }

            public InstanceOperation Operation
            {
                get;
                private set;
            }

            public WorkflowApplication Instance
            {
                get;
                private set;
            }
        }

        // This is a thin shell of PersistenceManager functionality so that
        // WorkflowApplicationInstance can hold onto a PM without exposing the entire persistence functionality
        internal abstract class PersistenceManagerBase
        {
            public abstract InstanceStore InstanceStore { get; }
            public abstract Guid InstanceId { get; }
        }

        private class PersistenceManager : PersistenceManagerBase
        {
            private InstanceHandle handle;
            private InstanceHandle temporaryHandle;
            private InstanceOwner owner;
            private bool ownerWasCreated;
            private bool isLocked;
            private bool aborted;
            private readonly bool isTryLoad;
            private Guid instanceId;
            private readonly InstanceStore store;

            // Initializing metadata, used when instance is created
            private IDictionary<XName, InstanceValue> instanceMetadata;

            // Updateable metadata, used when instance is saved
            private IDictionary<XName, InstanceValue> mutableMetadata;

            public PersistenceManager(InstanceStore store, IDictionary<XName, InstanceValue> instanceMetadata, Guid instanceId)
            {
                Fx.Assert(store != null, "We should never gets here without a store.");

                this.instanceId = instanceId;
                this.instanceMetadata = instanceMetadata;

                this.InitializeInstanceMetadata();

                this.owner = store.DefaultInstanceOwner;
                if (this.owner != null)
                {
                    this.handle = store.CreateInstanceHandle(this.owner, instanceId);
                }

                this.store = store;
            }

            public PersistenceManager(InstanceStore store, IDictionary<XName, InstanceValue> instanceMetadata)
            {
                Fx.Assert(store != null, "We should never get here without a store.");

                this.isTryLoad = true;
                this.instanceMetadata = instanceMetadata;

                this.InitializeInstanceMetadata();

                this.owner = store.DefaultInstanceOwner;
                if (this.owner != null)
                {
                    this.handle = store.CreateInstanceHandle(this.owner);
                }

                this.store = store;
            }

            public sealed override Guid InstanceId => this.instanceId;

            public sealed override InstanceStore InstanceStore => this.store;

            public bool IsInitialized => (this.handle != null);

            public bool IsLocked => this.isLocked;

            public bool OwnerWasCreated => this.ownerWasCreated;

            private void InitializeInstanceMetadata()
            {
                if (this.instanceMetadata == null)
                {
                    this.instanceMetadata = new Dictionary<XName, InstanceValue>(1);
                }

                // We always set this key explicitly so that users can't override this metadata value
                this.instanceMetadata[PersistenceMetadataNamespace.InstanceType] = new InstanceValue(WorkflowNamespace.WorkflowHostType, InstanceValueOptions.WriteOnly);
            }

            public void SetInstanceMetadata(IDictionary<XName, InstanceValue> metadata)
            {
                Fx.Assert(this.instanceMetadata.Count == 1, "We should only have the default metadata from InitializeInstanceMetadata");
                if (metadata != null)
                {
                    this.instanceMetadata = metadata;
                    this.InitializeInstanceMetadata();
                }
            }

            public void SetMutablemetadata(IDictionary<XName, InstanceValue> metadata) => this.mutableMetadata = metadata;

            public void Initialize(WorkflowIdentity definitionIdentity, TimeSpan timeout)
            {
                Fx.Assert(this.handle == null, "We are already initialized by now");

                using (new TransactionScope(TransactionScopeOption.Suppress))
                {
                    try
                    {
                        this.CreateTemporaryHandle(null);
                        this.owner = this.store.Execute(this.temporaryHandle, GetCreateOwnerCommand(definitionIdentity), timeout).InstanceOwner;
                        this.ownerWasCreated = true;
                    }
                    finally
                    {
                        this.FreeTemporaryHandle();
                    }

                    this.handle = this.isTryLoad ? this.store.CreateInstanceHandle(this.owner) : this.store.CreateInstanceHandle(this.owner, this.InstanceId);

                    Thread.MemoryBarrier();
                    if (this.aborted)
                    {
                        this.handle.Free();
                    }
                }
            }

            private void CreateTemporaryHandle(InstanceOwner owner)
            {
                this.temporaryHandle = this.store.CreateInstanceHandle(owner);

                Thread.MemoryBarrier();

                if (this.aborted)
                {
                    this.FreeTemporaryHandle();
                }
            }

            private void FreeTemporaryHandle()
            {
                var handle = this.temporaryHandle;

                if (handle != null)
                {
                    handle.Free();
                }
            }

            public IAsyncResult BeginInitialize(WorkflowIdentity definitionIdentity, TimeSpan timeout, AsyncCallback callback, object state)
            {
                Fx.Assert(this.handle == null, "We are already initialized by now");

                using (new TransactionScope(TransactionScopeOption.Suppress))
                {
                    IAsyncResult result = null;

                    try
                    {
                        this.CreateTemporaryHandle(null);
                        result = this.store.BeginExecute(this.temporaryHandle, GetCreateOwnerCommand(definitionIdentity), timeout, callback, state);
                    }
                    finally
                    {
                        // We've encountered an exception
                        if (result == null)
                        {
                            this.FreeTemporaryHandle();
                        }
                    }
                    return result;
                }
            }

            public void EndInitialize(IAsyncResult result)
            {
                try
                {
                    this.owner = this.store.EndExecute(result).InstanceOwner;
                    this.ownerWasCreated = true;
                }
                finally
                {
                    this.FreeTemporaryHandle();
                }

                this.handle = this.isTryLoad ? this.store.CreateInstanceHandle(this.owner) : this.store.CreateInstanceHandle(this.owner, this.InstanceId);
                Thread.MemoryBarrier();
                if (this.aborted)
                {
                    this.handle.Free();
                }
            }

            public void DeleteOwner(TimeSpan timeout)
            {
                try
                {
                    this.CreateTemporaryHandle(this.owner);
                    this.store.Execute(this.temporaryHandle, new DeleteWorkflowOwnerCommand(), timeout);
                }
                // Ignore some exceptions because DeleteWorkflowOwner is best effort.
                catch (InstancePersistenceCommandException) { }
                catch (InstanceOwnerException) { }
                catch (OperationCanceledException) { }
                finally
                {
                    this.FreeTemporaryHandle();
                }
            }

            public IAsyncResult BeginDeleteOwner(TimeSpan timeout, AsyncCallback callback, object state)
            {
                IAsyncResult result = null;
                try
                {
                    this.CreateTemporaryHandle(this.owner);
                    result = this.store.BeginExecute(this.temporaryHandle, new DeleteWorkflowOwnerCommand(), timeout, callback, state);
                }
                // Ignore some exceptions because DeleteWorkflowOwner is best effort.
                catch (InstancePersistenceCommandException) { }
                catch (InstanceOwnerException) { }
                catch (OperationCanceledException) { }
                finally
                {
                    if (result == null)
                    {
                        this.FreeTemporaryHandle();
                    }
                }
                return result;
            }

            public void EndDeleteOwner(IAsyncResult result)
            {
                try
                {
                    this.store.EndExecute(result);
                }
                // Ignore some exceptions because DeleteWorkflowOwner is best effort.
                catch (InstancePersistenceCommandException) { }
                catch (InstanceOwnerException) { }
                catch (OperationCanceledException) { }
                finally
                {
                    this.FreeTemporaryHandle();
                }
            }

            public void EnsureReadyness(TimeSpan timeout)
            {
                Fx.Assert(this.handle != null, "We should already be initialized by now");
                Fx.Assert(!this.IsLocked, "We are already ready for persistence; why are we being called?");
                Fx.Assert(!this.isTryLoad, "Should not be on an initial save path if we tried load.");

                using (new TransactionScope(TransactionScopeOption.Suppress))
                {
                    this.store.Execute(this.handle, CreateSaveCommand(null, this.instanceMetadata, PersistenceOperation.Save), timeout);
                    this.isLocked = true;
                }
            }

            public IAsyncResult BeginEnsureReadyness(TimeSpan timeout, AsyncCallback callback, object state)
            {
                Fx.Assert(this.handle != null, "We should already be initialized by now");
                Fx.Assert(!this.IsLocked, "We are already ready for persistence; why are we being called?");
                Fx.Assert(!this.isTryLoad, "Should not be on an initial save path if we tried load.");

                using (new TransactionScope(TransactionScopeOption.Suppress))
                {
                    return this.store.BeginExecute(this.handle, CreateSaveCommand(null, this.instanceMetadata, PersistenceOperation.Save), timeout, callback, state);
                }
            }

            public void EndEnsureReadyness(IAsyncResult result)
            {
                this.store.EndExecute(result);
                this.isLocked = true;
            }

            public static Dictionary<XName, InstanceValue> GenerateInitialData(WorkflowApplication instance)
            {
                var data = new Dictionary<XName, InstanceValue>(10);
                data[WorkflowNamespace.Bookmarks] = new InstanceValue(instance.Controller.GetBookmarks(), InstanceValueOptions.WriteOnly | InstanceValueOptions.Optional);
                data[WorkflowNamespace.LastUpdate] = new InstanceValue(DateTime.UtcNow, InstanceValueOptions.WriteOnly | InstanceValueOptions.Optional);

                foreach (var mappedVariable in instance.Controller.GetMappedVariables())
                {
                    data[WorkflowNamespace.VariablesPath.GetName(mappedVariable.Key)] = new InstanceValue(mappedVariable.Value, InstanceValueOptions.WriteOnly | InstanceValueOptions.Optional);
                }

                Fx.AssertAndThrow(instance.Controller.State != WorkflowInstanceState.Aborted, "Cannot generate data for an aborted instance.");
                if (instance.Controller.State != WorkflowInstanceState.Complete)
                {
                    data[WorkflowNamespace.Workflow] = new InstanceValue(instance.Controller.PrepareForSerialization());
                    data[WorkflowNamespace.Status] = new InstanceValue(instance.Controller.State == WorkflowInstanceState.Idle ? "Idle" : "Executing", InstanceValueOptions.WriteOnly);
                }
                else
                {
                    data[WorkflowNamespace.Workflow] = new InstanceValue(instance.Controller.PrepareForSerialization(), InstanceValueOptions.Optional);
                    var completionState = instance.Controller.GetCompletionState(out var outputs, out var completionException);

                    if (completionState == ActivityInstanceState.Faulted)
                    {
                        data[WorkflowNamespace.Status] = new InstanceValue("Faulted", InstanceValueOptions.WriteOnly);
                        data[WorkflowNamespace.Exception] = new InstanceValue(completionException, InstanceValueOptions.WriteOnly | InstanceValueOptions.Optional);
                    }
                    else if (completionState == ActivityInstanceState.Closed)
                    {
                        data[WorkflowNamespace.Status] = new InstanceValue("Closed", InstanceValueOptions.WriteOnly);
                        if (outputs != null)
                        {
                            foreach (var output in outputs)
                            {
                                data[WorkflowNamespace.OutputPath.GetName(output.Key)] = new InstanceValue(output.Value, InstanceValueOptions.WriteOnly | InstanceValueOptions.Optional);
                            }
                        }
                    }
                    else
                    {
                        Fx.AssertAndThrow(completionState == ActivityInstanceState.Canceled, "Cannot be executing when WorkflowState was completed.");
                        data[WorkflowNamespace.Status] = new InstanceValue("Canceled", InstanceValueOptions.WriteOnly);
                    }
                }
                return data;
            }

            private static InstancePersistenceCommand GetCreateOwnerCommand(WorkflowIdentity definitionIdentity)
            {
                // Technically, we only need to pass the owner identity when doing LoadRunnable.
                // However, if we create an instance with identity on a store that doesn't recognize
                // it, the identity metadata might be stored in a way which makes it unqueryable if
                // the store is later upgraded to support identity (e.g. SWIS 4.0 -> 4.5 upgrade).
                // So to be on the safe side, if we're using identity, we require the store to
                // explicitly support it.
                if (definitionIdentity != null)
                {
                    var result = new CreateWorkflowOwnerWithIdentityCommand();
                    if (!object.ReferenceEquals(definitionIdentity, WorkflowApplication.unknownIdentity))
                    {
                        result.InstanceOwnerMetadata.Add(Workflow45Namespace.DefinitionIdentities,
                            new InstanceValue(new Collection<WorkflowIdentity> { definitionIdentity }));
                    }
                    return result;
                }
                else
                {
                    return new CreateWorkflowOwnerCommand();
                }
            }

            private static SaveWorkflowCommand CreateSaveCommand(IDictionary<XName, InstanceValue> instance, IDictionary<XName, InstanceValue> instanceMetadata, PersistenceOperation operation)
            {
                var saveCommand = new SaveWorkflowCommand()
                {
                    CompleteInstance = operation == PersistenceOperation.Complete,
                    UnlockInstance = operation != PersistenceOperation.Save,
                };

                if (instance != null)
                {
                    foreach (var value in instance)
                    {
                        saveCommand.InstanceData.Add(value);
                    }
                }

                if (instanceMetadata != null)
                {
                    foreach (var value in instanceMetadata)
                    {
                        saveCommand.InstanceMetadataChanges.Add(value);
                    }
                }

                return saveCommand;
            }

            private bool TryLoadHelper(InstanceView view, out IDictionary<XName, InstanceValue> data)
            {
                if (!view.IsBoundToLock)
                {
                    data = null;
                    return false;
                }
                this.instanceId = view.InstanceId;
                this.isLocked = true;

                if (!this.handle.IsValid)
                {
                    throw FxTrace.Exception.AsError(new OperationCanceledException(SR.WorkflowInstanceAborted(this.InstanceId)));
                }

                data = view.InstanceData;
                return true;
            }

            public void Save(IDictionary<XName, InstanceValue> instance, PersistenceOperation operation, TimeSpan timeout)
            {
                this.store.Execute(this.handle, CreateSaveCommand(instance, (this.isLocked ? this.mutableMetadata : this.instanceMetadata), operation), timeout);
                this.isLocked = true;
            }

            public IDictionary<XName, InstanceValue> Load(TimeSpan timeout)
            {
                var view = this.store.Execute(this.handle, new LoadWorkflowCommand(), timeout);
                this.isLocked = true;

                if (!this.handle.IsValid)
                {
                    throw FxTrace.Exception.AsError(new OperationCanceledException(SR.WorkflowInstanceAborted(this.InstanceId)));
                }

                return view.InstanceData;
            }

            public bool TryLoad(TimeSpan timeout, out IDictionary<XName, InstanceValue> data)
            {
                var view = this.store.Execute(this.handle, new TryLoadRunnableWorkflowCommand(), timeout);
                return this.TryLoadHelper(view, out data);
            }

            public IAsyncResult BeginSave(IDictionary<XName, InstanceValue> instance, PersistenceOperation operation, TimeSpan timeout, AsyncCallback callback, object state) => this.store.BeginExecute(this.handle, CreateSaveCommand(instance, (this.isLocked ? this.mutableMetadata : this.instanceMetadata), operation), timeout, callback, state);

            public void EndSave(IAsyncResult result)
            {
                this.store.EndExecute(result);
                this.isLocked = true;
            }

            public IAsyncResult BeginLoad(TimeSpan timeout, AsyncCallback callback, object state) => this.store.BeginExecute(this.handle, new LoadWorkflowCommand(), timeout, callback, state);

            public IDictionary<XName, InstanceValue> EndLoad(IAsyncResult result)
            {
                var view = this.store.EndExecute(result);
                this.isLocked = true;

                if (!this.handle.IsValid)
                {
                    throw FxTrace.Exception.AsError(new OperationCanceledException(SR.WorkflowInstanceAborted(this.InstanceId)));
                }

                return view.InstanceData;
            }

            public IAsyncResult BeginTryLoad(TimeSpan timeout, AsyncCallback callback, object state) => this.store.BeginExecute(this.handle, new TryLoadRunnableWorkflowCommand(), timeout, callback, state);

            public bool EndTryLoad(IAsyncResult result, out IDictionary<XName, InstanceValue> data)
            {
                var view = this.store.EndExecute(result);
                return this.TryLoadHelper(view, out data);
            }

            public void Abort()
            {
                this.aborted = true;

                // Make sure the setter of handle sees aborted, or v.v., or both.
                Thread.MemoryBarrier();

                var handle = this.handle;
                if (handle != null)
                {
                    handle.Free();
                }

                this.FreeTemporaryHandle();
            }

            public void Unlock(TimeSpan timeout)
            {
                var saveCmd = new SaveWorkflowCommand()
                {
                    UnlockInstance = true,
                };

                this.store.Execute(this.handle, saveCmd, timeout);
            }

            public IAsyncResult BeginUnlock(TimeSpan timeout, AsyncCallback callback, object state)
            {
                var saveCmd = new SaveWorkflowCommand()
                {
                    UnlockInstance = true,
                };

                return this.store.BeginExecute(this.handle, saveCmd, timeout, callback, state);
            }

            public void EndUnlock(IAsyncResult result) => this.store.EndExecute(result);
        }
    }
}
