// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.DurableInstancing;
    using System.Activities.DynamicUpdate;
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
    public sealed partial class WorkflowApplication : WorkflowInstance
    {
        /// <summary>
        /// The unknown identity
        /// </summary>
        private static readonly WorkflowIdentity unknownIdentity = new WorkflowIdentity();

        /// <summary>
        /// The completed handler
        /// </summary>
        private static CompletedEventHandler? completedHandler;

        /// <summary>
        /// The event frame callback
        /// </summary>
        private static AsyncCallback? eventFrameCallback;

        /// <summary>
        /// The idle handler
        /// </summary>
        private static IdleEventHandler? idleHandler;

        /// <summary>
        /// The unhandled exception handler
        /// </summary>
        private static UnhandledExceptionEventHandler? unhandledExceptionHandler;

        /// <summary>
        /// The wait asynchronous complete callback
        /// </summary>
        private static Action<object, TimeoutException>? waitAsyncCompleteCallback;

        /// <summary>
        /// The initial workflow arguments
        /// </summary>
        /// <remarks>Initial creation data</remarks>
        private readonly IDictionary<string, object>? initialWorkflowArguments;

        /// <summary>
        /// The pending operations
        /// </summary>
        private readonly Quack<InstanceOperation> pendingOperations;

        /// <summary>
        /// The root execution properties
        /// </summary>
        private readonly IList<Handle>? rootExecutionProperties;

        // We use this to keep track of the number of "interesting" things that have happened.
        // Notifying operations and calling Run on the runtime count as interesting things. All
        // operations are stamped with the actionCount at the time of being enqueued.
        /// <summary>
        /// The action count
        /// </summary>
        private int actionCount;

        /// <summary>
        /// The event data
        /// </summary>
        private WorkflowEventData? eventData;

        /// <summary>
        /// The extensions
        /// </summary>
        private WorkflowInstanceExtensionManager? extensions;

        /// <summary>
        /// The handler thread identifier
        /// </summary>
        private int handlerThreadId;

        /// <summary>
        /// Tracking for one-time actions per in-memory pulse
        /// </summary>
        private bool hasCalledAbort;

        /// <summary>
        /// The has called run
        /// </summary>
        private bool hasCalledRun;

        /// <summary>
        /// The has execution occurred since last idle
        /// </summary>
        private bool hasExecutionOccurredSinceLastIdle;

        /// <summary>
        /// Tracking for one-time actions per instance lifetime (these end up being persisted)
        /// </summary>
        private bool hasRaisedCompleted;

        /// <summary>
        /// The instance identifier
        /// </summary>
        private Guid instanceId;

        /// <summary>
        /// The instance identifier set
        /// </summary>
        private bool instanceIdSet;

        /// <summary>
        /// The instance metadata
        /// </summary>
        private IDictionary<XName, InstanceValue>? instanceMetadata;

        /// <summary>
        /// The instance store
        /// </summary>
        private InstanceStore? instanceStore;

        /// <summary>
        /// The invoke completed callback
        /// </summary>
        private Action? invokeCompletedCallback;

        /// <summary>
        /// Checking for Guid.Empty is expensive.
        /// </summary>
        private bool isBusy;

        /// <summary>
        /// The is in handler
        /// </summary>
        private bool isInHandler;

        /// <summary>
        /// The on aborted
        /// </summary>
        private Action<WorkflowApplicationAbortedEventArgs>? onAborted;

        /// <summary>
        /// The on completed
        /// </summary>
        private Action<WorkflowApplicationCompletedEventArgs>? onCompleted;

        /// <summary>
        /// The on idle
        /// </summary>
        private Action<WorkflowApplicationIdleEventArgs>? onIdle;

        /// <summary>
        /// The on persistable idle
        /// </summary>
        private Func<WorkflowApplicationIdleEventArgs, PersistableIdleAction>? onPersistableIdle;

        /// <summary>
        /// The on unhandled exception
        /// </summary>
        private Func<WorkflowApplicationUnhandledExceptionEventArgs, UnhandledExceptionAction>? onUnhandledException;

        /// <summary>
        /// The on unloaded
        /// </summary>
        private Action<WorkflowApplicationEventArgs>? onUnloaded;

        /// <summary>
        /// Count of operations that are about to be enqueued. We use this when enqueueing multiple
        /// operations, to avoid raising idle on dequeue of the first operation.
        /// </summary>
        private int pendingUnenqueued;

        /// <summary>
        /// The persistence manager
        /// </summary>
        private PersistenceManager? persistenceManager;

        /// <summary>
        /// The persistence pipeline in use
        /// </summary>
        private PersistencePipeline? persistencePipelineInUse;

        /// <summary>
        /// The state
        /// </summary>
        private WorkflowApplicationState state;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowApplication" /> class.
        /// </summary>
        /// <param name="workflowDefinition">The workflow definition.</param>
        public WorkflowApplication(Activity workflowDefinition)
            : this(workflowDefinition, (WorkflowIdentity?)null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowApplication" /> class.
        /// </summary>
        /// <param name="workflowDefinition">The workflow definition.</param>
        /// <param name="inputs">The inputs.</param>
        public WorkflowApplication(Activity workflowDefinition, IDictionary<string, object> inputs)
            : this(workflowDefinition, inputs, (WorkflowIdentity?)null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowApplication" /> class.
        /// </summary>
        /// <param name="workflowDefinition">The workflow definition.</param>
        /// <param name="definitionIdentity">The definition identity.</param>
        public WorkflowApplication(Activity workflowDefinition, WorkflowIdentity? definitionIdentity)
            : base(workflowDefinition, definitionIdentity)
        {
            this.pendingOperations = new Quack<InstanceOperation>();
            Fx.Assert(this.state == WorkflowApplicationState.Paused, "We always start out paused (the default)");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowApplication" /> class.
        /// </summary>
        /// <param name="workflowDefinition">The workflow definition.</param>
        /// <param name="inputs">The inputs.</param>
        /// <param name="definitionIdentity">The definition identity.</param>
        public WorkflowApplication(Activity workflowDefinition, IDictionary<string, object> inputs, WorkflowIdentity definitionIdentity)
            : this(workflowDefinition, definitionIdentity) => this.initialWorkflowArguments = inputs ?? throw FxTrace.Exception.ArgumentNull(nameof(inputs));

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowApplication" /> class.
        /// </summary>
        /// <param name="workflowDefinition">The workflow definition.</param>
        /// <param name="inputs">The inputs.</param>
        /// <param name="executionProperties">The execution properties.</param>
        private WorkflowApplication(Activity workflowDefinition, IDictionary<string, object> inputs, IList<Handle> executionProperties)
            : this(workflowDefinition)
        {
            this.initialWorkflowArguments = inputs;
            this.rootExecutionProperties = executionProperties;
        }

        /// <summary>
        /// Gets or sets the aborted.
        /// </summary>
        /// <value>The aborted.</value>
        public Action<WorkflowApplicationAbortedEventArgs> Aborted
        {
            get => this.onAborted;
            set
            {
                this.ThrowIfMulticast(value);
                this.onAborted = value;
            }
        }

        /// <summary>
        /// Gets or sets the completed.
        /// </summary>
        /// <value>The completed.</value>
        public Action<WorkflowApplicationCompletedEventArgs> Completed
        {
            get => this.onCompleted;
            set
            {
                this.ThrowIfMulticast(value);
                this.onCompleted = value;
            }
        }

        /// <summary>
        /// Gets the extensions.
        /// </summary>
        /// <value>The extensions.</value>
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

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
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

        /// <summary>
        /// Gets or sets the idle.
        /// </summary>
        /// <value>The idle.</value>
        public Action<WorkflowApplicationIdleEventArgs> Idle
        {
            get => this.onIdle;
            set
            {
                this.ThrowIfMulticast(value);
                this.onIdle = value;
            }
        }

        /// <summary>
        /// Gets or sets the instance store.
        /// </summary>
        /// <value>The instance store.</value>
        public InstanceStore InstanceStore
        {
            get => this.instanceStore;
            set
            {
                this.ThrowIfReadOnly();
                this.instanceStore = value;
            }
        }

        /// <summary>
        /// Gets or sets the on unhandled exception.
        /// </summary>
        /// <value>The on unhandled exception.</value>
        public Func<WorkflowApplicationUnhandledExceptionEventArgs, UnhandledExceptionAction> OnUnhandledException
        {
            get => this.onUnhandledException;
            set
            {
                this.ThrowIfMulticast(value);
                this.onUnhandledException = value;
            }
        }

        /// <summary>
        /// Gets or sets the persistable idle.
        /// </summary>
        /// <value>The persistable idle.</value>
        public Func<WorkflowApplicationIdleEventArgs, PersistableIdleAction> PersistableIdle
        {
            get => this.onPersistableIdle;
            set
            {
                this.ThrowIfMulticast(value);
                this.onPersistableIdle = value;
            }
        }

        /// <summary>
        /// Gets or sets the unloaded.
        /// </summary>
        /// <value>The unloaded.</value>
        public Action<WorkflowApplicationEventArgs> Unloaded
        {
            get => this.onUnloaded;
            set
            {
                this.ThrowIfMulticast(value);
                this.onUnloaded = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether [supports instance keys].
        /// </summary>
        /// <value><c>true</c> if [supports instance keys]; otherwise, <c>false</c>.</value>
        protected internal override bool SupportsInstanceKeys => false;

        /// <summary>
        /// Gets the event frame callback.
        /// </summary>
        /// <value>The event frame callback.</value>
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

        /// <summary>
        /// Gets the event data.
        /// </summary>
        /// <value>The event data.</value>
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

        /// <summary>
        /// Gets a value indicating whether this instance has persistence provider.
        /// </summary>
        /// <value><c>true</c> if this instance has persistence provider; otherwise, <c>false</c>.</value>
        private bool HasPersistenceProvider => this.persistenceManager != null;

        /// <summary>
        /// Gets a value indicating whether this instance is handler thread.
        /// </summary>
        /// <value><c>true</c> if this instance is handler thread; otherwise, <c>false</c>.</value>
        private bool IsHandlerThread => this.isInHandler && this.handlerThreadId == Thread.CurrentThread.ManagedThreadId;

        /// <summary>
        /// Gets a value indicating whether this instance is in terminal state.
        /// </summary>
        /// <value><c>true</c> if this instance is in terminal state; otherwise, <c>false</c>.</value>
        private bool IsInTerminalState => this.state == WorkflowApplicationState.Unloaded || this.state == WorkflowApplicationState.Aborted;

        /// <summary>
        /// Begins the create default instance owner.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="definitionIdentity">The definition identity.</param>
        /// <param name="identityFilter">The identity filter.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public static IAsyncResult BeginCreateDefaultInstanceOwner(InstanceStore instanceStore, WorkflowIdentity definitionIdentity,
            WorkflowIdentityFilter identityFilter, AsyncCallback callback, object state) => BeginCreateDefaultInstanceOwner(instanceStore, definitionIdentity, identityFilter, ActivityDefaults.OpenTimeout, callback, state);

        /// <summary>
        /// Begins the create default instance owner.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="definitionIdentity">The definition identity.</param>
        /// <param name="identityFilter">The identity filter.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
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

        /// <summary>
        /// Begins the delete default instance owner.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public static IAsyncResult BeginDeleteDefaultInstanceOwner(InstanceStore instanceStore, AsyncCallback callback, object state) => BeginDeleteDefaultInstanceOwner(instanceStore, ActivityDefaults.CloseTimeout, callback, state);

        /// <summary>
        /// Begins the delete default instance owner.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
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

        /// <summary>
        /// Begins the get instance.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public static IAsyncResult BeginGetInstance(Guid instanceId, InstanceStore instanceStore, AsyncCallback callback, object state) => BeginGetInstance(instanceId, instanceStore, ActivityDefaults.LoadTimeout, callback, state);

        /// <summary>
        /// Begins the get instance.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
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

        /// <summary>
        /// Begins the get runnable instance.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public static IAsyncResult BeginGetRunnableInstance(InstanceStore instanceStore, AsyncCallback callback, object state) => BeginGetRunnableInstance(instanceStore, ActivityDefaults.LoadTimeout, callback, state);

        /// <summary>
        /// Begins the get runnable instance.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        /// <exception cref="InvalidOperationException"></exception>
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

        /// <summary>
        /// Creates the default instance owner.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="definitionIdentity">The definition identity.</param>
        /// <param name="identityFilter">The identity filter.</param>
        public static void CreateDefaultInstanceOwner(InstanceStore instanceStore, WorkflowIdentity definitionIdentity, WorkflowIdentityFilter identityFilter) => CreateDefaultInstanceOwner(instanceStore, definitionIdentity, identityFilter, ActivityDefaults.OpenTimeout);

        /// <summary>
        /// Creates the default instance owner.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="definitionIdentity">The definition identity.</param>
        /// <param name="identityFilter">The identity filter.</param>
        /// <param name="timeout">The timeout.</param>
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

        /// <summary>
        /// Deletes the default instance owner.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        public static void DeleteDefaultInstanceOwner(InstanceStore instanceStore) => DeleteDefaultInstanceOwner(instanceStore, ActivityDefaults.CloseTimeout);

        /// <summary>
        /// Deletes the default instance owner.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="timeout">The timeout.</param>
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

        /// <summary>
        /// Ends the create default instance owner.
        /// </summary>
        /// <param name="asyncResult">The asynchronous result.</param>
        public static void EndCreateDefaultInstanceOwner(IAsyncResult asyncResult)
        {
            InstanceCommandWithTemporaryHandleAsyncResult.End(asyncResult, out var instanceStore, out var commandResult);
            instanceStore.DefaultInstanceOwner = commandResult.InstanceOwner;
        }

        /// <summary>
        /// Ends the delete default instance owner.
        /// </summary>
        /// <param name="asyncResult">The asynchronous result.</param>
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

        /// <summary>
        /// Ends the get instance.
        /// </summary>
        /// <param name="asyncResult">The asynchronous result.</param>
        /// <returns>WorkflowApplicationInstance.</returns>
        public static WorkflowApplicationInstance EndGetInstance(IAsyncResult asyncResult) => LoadAsyncResult.EndAndCreateInstance(asyncResult);

        /// <summary>
        /// Ends the get runnable instance.
        /// </summary>
        /// <param name="asyncResult">The asynchronous result.</param>
        /// <returns>WorkflowApplicationInstance.</returns>
        public static WorkflowApplicationInstance EndGetRunnableInstance(IAsyncResult asyncResult) => LoadAsyncResult.EndAndCreateInstance(asyncResult);

        /// <summary>
        /// Gets the instance.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="instanceStore">The instance store.</param>
        /// <returns>WorkflowApplicationInstance.</returns>
        public static WorkflowApplicationInstance GetInstance(Guid instanceId, InstanceStore instanceStore) => GetInstance(instanceId, instanceStore, ActivityDefaults.LoadTimeout);

        /// <summary>
        /// Gets the instance.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns>WorkflowApplicationInstance.</returns>
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

        /// <summary>
        /// Gets the runnable instance.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <returns>WorkflowApplicationInstance.</returns>
        public static WorkflowApplicationInstance GetRunnableInstance(InstanceStore instanceStore) => GetRunnableInstance(instanceStore, ActivityDefaults.LoadTimeout);

        /// <summary>
        /// Gets the runnable instance.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns>WorkflowApplicationInstance.</returns>
        /// <exception cref="InvalidOperationException"></exception>
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

        /// <summary>
        /// Aborts this instance.
        /// </summary>
        public void Abort() => this.Abort(SR.DefaultAbortReason);

        /// <summary>
        /// Aborts the specified reason.
        /// </summary>
        /// <param name="reason">The reason.</param>
        public void Abort(string reason) => this.Abort(reason, null);

        /// <summary>
        /// Adds the initial instance values.
        /// </summary>
        /// <param name="writeOnlyValues">The write only values.</param>
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

        /// <summary>
        /// Begins the cancel.
        /// </summary>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginCancel(AsyncCallback callback, object state) => this.BeginCancel(ActivityDefaults.AcquireLockTimeout, callback, state);

        /// <summary>
        /// Begins the cancel.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginCancel(TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            return CancelAsyncResult.Create(this, timeout, callback, state);
        }

        /// <summary>
        /// Begins the load.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginLoad(Guid instanceId, AsyncCallback callback, object state) => this.BeginLoad(instanceId, ActivityDefaults.LoadTimeout, callback, state);

        /// <summary>
        /// Begins the load.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        /// <exception cref="InvalidOperationException"></exception>
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

        /// <summary>
        /// Begins the load.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginLoad(WorkflowApplicationInstance instance, AsyncCallback callback, object state) => this.BeginLoad(instance, null, ActivityDefaults.LoadTimeout, callback, state);

        /// <summary>
        /// Begins the load.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginLoad(WorkflowApplicationInstance instance, TimeSpan timeout,
                    AsyncCallback callback, object state) => this.BeginLoad(instance, null, timeout, callback, state);

        /// <summary>
        /// Begins the load.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="updateMap">The update map.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginLoad(WorkflowApplicationInstance instance, DynamicUpdateMap updateMap,
                    AsyncCallback callback, object state) => this.BeginLoad(instance, updateMap, ActivityDefaults.LoadTimeout, callback, state);

        /// <summary>
        /// Begins the load.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="updateMap">The update map.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public IAsyncResult BeginLoad(WorkflowApplicationInstance instance, DynamicUpdateMap updateMap, TimeSpan timeout,
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

            return new LoadAsyncResult(this, newManager, instance.Values, updateMap, timeout, callback, state);
        }

        /// <summary>
        /// Begins the load runnable instance.
        /// </summary>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginLoadRunnableInstance(AsyncCallback callback, object state) => this.BeginLoadRunnableInstance(ActivityDefaults.LoadTimeout, callback, state);

        /// <summary>
        /// Begins the load runnable instance.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        /// <exception cref="InvalidOperationException"></exception>
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

        /// <summary>
        /// Begins the persist.
        /// </summary>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        [Fx.Tag.InheritThrows(From = "Unload")]
        public IAsyncResult BeginPersist(AsyncCallback callback, object state) => this.BeginPersist(ActivityDefaults.SaveTimeout, callback, state);

        /// <summary>
        /// Begins the persist.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        [Fx.Tag.InheritThrows(From = "Unload")]
        public IAsyncResult BeginPersist(TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            return new UnloadOrPersistAsyncResult(this, timeout, PersistenceOperation.Save, false, false, callback, state);
        }

        /// <summary>
        /// Begins the resume bookmark.
        /// </summary>
        /// <param name="bookmarkName">Name of the bookmark.</param>
        /// <param name="value">The value.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public IAsyncResult BeginResumeBookmark(string bookmarkName, object value, AsyncCallback callback, object state)
        {
            if (string.IsNullOrEmpty(bookmarkName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(bookmarkName));
            }

            return this.BeginResumeBookmark(new Bookmark(bookmarkName), value, callback, state);
        }

        /// <summary>
        /// Begins the resume bookmark.
        /// </summary>
        /// <param name="bookmarkName">Name of the bookmark.</param>
        /// <param name="value">The value.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public IAsyncResult BeginResumeBookmark(string bookmarkName, object value, TimeSpan timeout, AsyncCallback callback, object state)
        {
            if (string.IsNullOrEmpty(bookmarkName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(bookmarkName));
            }

            return this.BeginResumeBookmark(new Bookmark(bookmarkName), value, timeout, callback, state);
        }

        /// <summary>
        /// Begins the resume bookmark.
        /// </summary>
        /// <param name="bookmark">The bookmark.</param>
        /// <param name="value">The value.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public IAsyncResult BeginResumeBookmark(Bookmark bookmark, object value, AsyncCallback callback, object state) => this.BeginResumeBookmark(bookmark, value, ActivityDefaults.ResumeBookmarkTimeout, callback, state);

        /// <summary>
        /// Begins the resume bookmark.
        /// </summary>
        /// <param name="bookmark">The bookmark.</param>
        /// <param name="value">The value.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public IAsyncResult BeginResumeBookmark(Bookmark bookmark, object value, TimeSpan timeout, AsyncCallback callback, object state)
        {
            TimeoutHelper.ThrowIfNegativeArgument(timeout);
            this.ThrowIfHandlerThread();

            return new ResumeBookmarkAsyncResult(this, bookmark, value, timeout, callback, state);
        }

        /// <summary>
        /// Begins the run.
        /// </summary>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginRun(AsyncCallback callback, object state) => this.BeginRun(ActivityDefaults.AcquireLockTimeout, callback, state);

        /// <summary>
        /// Begins the run.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginRun(TimeSpan timeout, AsyncCallback callback, object state) => this.BeginInternalRun(timeout, true, callback, state);

        /// <summary>
        /// Begins the terminate.
        /// </summary>
        /// <param name="reason">The reason.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginTerminate(string reason, AsyncCallback callback, object state) => this.BeginTerminate(reason, ActivityDefaults.AcquireLockTimeout, callback, state);

        /// <summary>
        /// Begins the terminate.
        /// </summary>
        /// <param name="reason">The reason.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginTerminate(Exception reason, AsyncCallback callback, object state) => this.BeginTerminate(reason, ActivityDefaults.AcquireLockTimeout, callback, state);

        /// <summary>
        /// Begins the terminate.
        /// </summary>
        /// <param name="reason">The reason.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        public IAsyncResult BeginTerminate(string reason, TimeSpan timeout, AsyncCallback callback, object state)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(reason));
            }

            return this.BeginTerminate(new WorkflowApplicationTerminatedException(reason, this.Id), timeout, callback, state);
        }

        /// <summary>
        /// Begins the terminate.
        /// </summary>
        /// <param name="reason">The reason.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
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

        /// <summary>
        /// Begins the unload.
        /// </summary>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        [Fx.Tag.InheritThrows(From = "Unload")]
        public IAsyncResult BeginUnload(AsyncCallback callback, object state) => this.BeginUnload(ActivityDefaults.SaveTimeout, callback, state);

        /// <summary>
        /// Begins the unload.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        [Fx.Tag.InheritThrows(From = "Unload")]
        public IAsyncResult BeginUnload(TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            return new UnloadOrPersistAsyncResult(this, timeout, PersistenceOperation.Unload, false, false, callback, state);
        }

        /// <summary>
        /// Cancels this instance.
        /// </summary>
        public void Cancel() => this.Cancel(ActivityDefaults.AcquireLockTimeout);

        /// <summary>
        /// Cancels the specified timeout.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
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

        /// <summary>
        /// Ends the cancel.
        /// </summary>
        /// <param name="result">The result.</param>
        public void EndCancel(IAsyncResult result) => CancelAsyncResult.End(result);

        /// <summary>
        /// Ends the load.
        /// </summary>
        /// <param name="result">The result.</param>
        public void EndLoad(IAsyncResult result) => LoadAsyncResult.End(result);

        /// <summary>
        /// Ends the load runnable instance.
        /// </summary>
        /// <param name="result">The result.</param>
        public void EndLoadRunnableInstance(IAsyncResult result) => LoadAsyncResult.End(result);

        /// <summary>
        /// Ends the persist.
        /// </summary>
        /// <param name="result">The result.</param>
        [Fx.Tag.InheritThrows(From = "Unload")]
        public void EndPersist(IAsyncResult result) => UnloadOrPersistAsyncResult.End(result);

        /// <summary>
        /// Ends the resume bookmark.
        /// </summary>
        /// <param name="result">The result.</param>
        /// <returns>BookmarkResumptionResult.</returns>
        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public BookmarkResumptionResult EndResumeBookmark(IAsyncResult result) => ResumeBookmarkAsyncResult.End(result);

        /// <summary>
        /// Ends the run.
        /// </summary>
        /// <param name="result">The result.</param>
        public void EndRun(IAsyncResult result) => RunAsyncResult.End(result);

        /// <summary>
        /// Ends the terminate.
        /// </summary>
        /// <param name="result">The result.</param>
        public void EndTerminate(IAsyncResult result) => TerminateAsyncResult.End(result);

        /// <summary>
        /// Ends the unload.
        /// </summary>
        /// <param name="result">The result.</param>
        [Fx.Tag.InheritThrows(From = "Unload")]
        public void EndUnload(IAsyncResult result) => UnloadOrPersistAsyncResult.End(result);

        /// <summary>
        /// Gets the bookmarks.
        /// </summary>
        /// <returns>ReadOnlyCollection&lt;BookmarkInfo&gt;.</returns>
        public ReadOnlyCollection<BookmarkInfo> GetBookmarks() => this.GetBookmarks(ActivityDefaults.ResumeBookmarkTimeout);

        /// <summary>
        /// Gets the bookmarks.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <returns>ReadOnlyCollection&lt;BookmarkInfo&gt;.</returns>
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

        /// <summary>
        /// Loads the specified instance.
        /// </summary>
        /// <param name="instance">The instance.</param>
        public void Load(WorkflowApplicationInstance instance) => this.Load(instance, ActivityDefaults.LoadTimeout);

        /// <summary>
        /// Loads the specified instance.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="timeout">The timeout.</param>
        /// <exception cref="InvalidOperationException"></exception>
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

        /// <summary>
        /// Loads the specified instance.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="updateMap">The update map.</param>
        public void Load(WorkflowApplicationInstance instance, DynamicUpdateMap updateMap) => this.Load(instance, updateMap, ActivityDefaults.LoadTimeout);

        /// <summary>
        /// Loads the specified instance.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="updateMap">The update map.</param>
        /// <param name="timeout">The timeout.</param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Load(WorkflowApplicationInstance instance, DynamicUpdateMap? updateMap, TimeSpan timeout)
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

                this.LoadCore(updateMap, timeoutHelper, false, instance.Values);
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

        /// <summary>
        /// Loads the specified instance identifier.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        public void Load(Guid instanceId) => this.Load(instanceId, ActivityDefaults.LoadTimeout);

        /// <summary>
        /// Loads the specified instance identifier.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="timeout">The timeout.</param>
        /// <exception cref="InvalidOperationException"></exception>
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

                this.LoadCore(null, timeoutHelper, false);
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

        /// <summary>
        /// Loads the runnable instance.
        /// </summary>
        public void LoadRunnableInstance() => this.LoadRunnableInstance(ActivityDefaults.LoadTimeout);

        /// <summary>
        /// Loads the runnable instance.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <exception cref="InvalidOperationException"></exception>
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

                this.LoadCore(null, timeoutHelper, true);
            }
            finally
            {
                this.NotifyOperationComplete(operation);
            }
        }

        /// <summary>
        /// Persists this instance.
        /// </summary>
        [Fx.Tag.InheritThrows(From = "Unload")]
        public void Persist() => this.Persist(ActivityDefaults.SaveTimeout);

        /// <summary>
        /// Persists the specified timeout.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
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

        /// <summary>
        /// Resumes the bookmark.
        /// </summary>
        /// <param name="bookmarkName">Name of the bookmark.</param>
        /// <param name="value">The value.</param>
        /// <returns>BookmarkResumptionResult.</returns>
        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public BookmarkResumptionResult ResumeBookmark(string bookmarkName, object value)
        {
            if (string.IsNullOrEmpty(bookmarkName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(bookmarkName));
            }

            return this.ResumeBookmark(new Bookmark(bookmarkName), value);
        }

        /// <summary>
        /// Resumes the bookmark.
        /// </summary>
        /// <param name="bookmark">The bookmark.</param>
        /// <param name="value">The value.</param>
        /// <returns>BookmarkResumptionResult.</returns>
        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public BookmarkResumptionResult ResumeBookmark(Bookmark bookmark, object value) => this.ResumeBookmark(bookmark, value, ActivityDefaults.ResumeBookmarkTimeout);

        /// <summary>
        /// Resumes the bookmark.
        /// </summary>
        /// <param name="bookmarkName">Name of the bookmark.</param>
        /// <param name="value">The value.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns>BookmarkResumptionResult.</returns>
        [Fx.Tag.InheritThrows(From = "ResumeBookmark")]
        public BookmarkResumptionResult ResumeBookmark(string bookmarkName, object value, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(bookmarkName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(bookmarkName));
            }

            return this.ResumeBookmark(new Bookmark(bookmarkName), value, timeout);
        }

        /// <summary>
        /// Resumes the bookmark.
        /// </summary>
        /// <param name="bookmark">The bookmark.</param>
        /// <param name="value">The value.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns>BookmarkResumptionResult.</returns>
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

        /// <summary>
        /// Runs this instance.
        /// </summary>
        public void Run() => this.Run(ActivityDefaults.AcquireLockTimeout);

        /// <summary>
        /// Runs the specified timeout.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        public void Run(TimeSpan timeout) => this.InternalRun(timeout, true);

        /// <summary>
        /// Terminates the specified reason.
        /// </summary>
        /// <param name="reason">The reason.</param>
        public void Terminate(string reason) => this.Terminate(reason, ActivityDefaults.AcquireLockTimeout);

        /// <summary>
        /// Terminates the specified reason.
        /// </summary>
        /// <param name="reason">The reason.</param>
        public void Terminate(Exception reason) => this.Terminate(reason, ActivityDefaults.AcquireLockTimeout);

        /// <summary>
        /// Terminates the specified reason.
        /// </summary>
        /// <param name="reason">The reason.</param>
        /// <param name="timeout">The timeout.</param>
        public void Terminate(string reason, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(reason));
            }

            this.Terminate(new WorkflowApplicationTerminatedException(reason, this.Id), timeout);
        }

        /// <summary>
        /// Terminates the specified reason.
        /// </summary>
        /// <param name="reason">The reason.</param>
        /// <param name="timeout">The timeout.</param>
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

        /// <summary>
        /// Unloads this instance.
        /// </summary>
        [Fx.Tag.Throws(typeof(WorkflowApplicationException), "The WorkflowApplication is in a state for which unloading is not valid.  The specific subclass denotes which state the instance is in.")]
        [Fx.Tag.Throws(typeof(InstancePersistenceException), "Something went wrong during persistence, but persistence can be retried.")]
        [Fx.Tag.Throws(typeof(TimeoutException), "The workflow could not be unloaded within the given timeout.")]
        public void Unload() => this.Unload(ActivityDefaults.SaveTimeout);

        /// <summary>
        /// Unloads the specified timeout.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
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

        /// <summary>
        /// Begins the discard instance.
        /// </summary>
        /// <param name="persistanceManager">The persistance manager.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        internal static IAsyncResult BeginDiscardInstance(PersistenceManagerBase persistanceManager, TimeSpan timeout, AsyncCallback callback, object state)
        {
            var manager = (PersistenceManager)persistanceManager;
            var timeoutHelper = new TimeoutHelper(timeout);
            return new UnlockInstanceAsyncResult(manager, timeoutHelper, callback, state);
        }

        /// <summary>
        /// Begins the invoke.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <param name="inputs">The inputs.</param>
        /// <param name="extensions">The extensions.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="syncContext">The synchronize context.</param>
        /// <param name="invokeContext">The invoke context.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        internal static IAsyncResult BeginInvoke(Activity activity, IDictionary<string, object> inputs, WorkflowInstanceExtensionManager extensions, TimeSpan timeout, SynchronizationContext syncContext, AsyncInvokeContext invokeContext, AsyncCallback callback, object state)
        {
            Fx.Assert(activity != null, "The activity must not be null.");

            return new InvokeAsyncResult(activity, inputs, extensions, timeout, syncContext, invokeContext, callback, state);
        }

        /// <summary>
        /// Discards the instance.
        /// </summary>
        /// <param name="persistanceManager">The persistance manager.</param>
        /// <param name="timeout">The timeout.</param>
        internal static void DiscardInstance(PersistenceManagerBase persistanceManager, TimeSpan timeout)
        {
            var manager = (PersistenceManager)persistanceManager;
            var timeoutHelper = new TimeoutHelper(timeout);
            UnlockInstance(manager, timeoutHelper);
        }

        /// <summary>
        /// Ends the discard instance.
        /// </summary>
        /// <param name="asyncResult">The asynchronous result.</param>
        internal static void EndDiscardInstance(IAsyncResult asyncResult) => UnlockInstanceAsyncResult.End(asyncResult);

        /// <summary>
        /// Ends the invoke.
        /// </summary>
        /// <param name="result">The result.</param>
        /// <returns>IDictionary&lt;System.String, System.Object&gt;.</returns>
        internal static IDictionary<string, object>? EndInvoke(IAsyncResult result) => InvokeAsyncResult.End(result);

        /// <summary>
        /// Gets the activities blocking update.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="updateMap">The update map.</param>
        /// <returns>IList&lt;ActivityBlockingUpdate&gt;.</returns>
        internal static IList<ActivityBlockingUpdate> GetActivitiesBlockingUpdate(WorkflowApplicationInstance instance, DynamicUpdateMap updateMap)
        {
            object deserializedRuntimeState = ExtractRuntimeState(instance.Values, instance.InstanceId);
            return WorkflowInstance.GetActivitiesBlockingUpdate(deserializedRuntimeState, updateMap);
        }

        /// <summary>
        /// Invokes the specified activity.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <param name="inputs">The inputs.</param>
        /// <param name="extensions">The extensions.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns>IDictionary&lt;System.String, System.Object&gt;.</returns>
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

        /// <summary>
        /// Gets the bookmarks for idle.
        /// </summary>
        /// <returns>ReadOnlyCollection&lt;BookmarkInfo&gt;.</returns>
        /// <remarks>
        /// called from WorkflowApplicationIdleEventArgs
        /// </remarks>
        internal ReadOnlyCollection<BookmarkInfo> GetBookmarksForIdle() => this.Controller.GetBookmarks();

        /// <summary>
        /// Gets the completion status.
        /// </summary>
        /// <param name="terminationException">The termination exception.</param>
        /// <param name="cancelled">if set to <c>true</c> [cancelled].</param>
        /// <remarks>
        /// used by WorkflowInvoker in the InvokeAsync case
        /// </remarks>
        internal void GetCompletionStatus(out Exception terminationException, out bool cancelled)
        {
            var completionState = this.Controller.GetCompletionState(out var dummyOutputs, out terminationException);
            Fx.Assert(completionState != ActivityInstanceState.Executing, "Activity cannot be executing when this method is called");
            cancelled = (completionState == ActivityInstanceState.Canceled);
        }

        /// <summary>
        /// Internals the get extensions.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>IEnumerable&lt;T&gt;.</returns>
        /// <remarks>
        /// host-facing access to our cascading ExtensionManager resolution. Used by WorkflowApplicationEventArgs
        /// </remarks>
        internal IEnumerable<T?> InternalGetExtensions<T>() where T : class => base.GetExtensions<T>();

        /// <summary>
        /// Called when [begin associate keys].
        /// </summary>
        /// <param name="keys">The keys.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        protected internal override IAsyncResult OnBeginAssociateKeys(ICollection<InstanceKey> keys, AsyncCallback callback, object state) => throw Fx.AssertAndThrow("WorkflowApplication is sealed with CanUseKeys as false, so WorkflowInstance should not call OnBeginAssociateKeys.");

        /// <summary>
        /// Called when [begin persist].
        /// </summary>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        protected internal override IAsyncResult OnBeginPersist(AsyncCallback callback, object state) => this.BeginInternalPersist(PersistenceOperation.Save, ActivityDefaults.InternalSaveTimeout, true, callback, state);

        /// <summary>
        /// Called when [begin resume bookmark].
        /// </summary>
        /// <param name="bookmark">The bookmark.</param>
        /// <param name="value">The value.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        protected internal override IAsyncResult OnBeginResumeBookmark(Bookmark bookmark, object value, TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.ThrowIfHandlerThread();
            return new ResumeBookmarkAsyncResult(this, bookmark, value, true, timeout, callback, state);
        }

        /// <summary>
        /// Called when [disassociate keys].
        /// </summary>
        /// <param name="keys">The keys.</param>
        protected internal override void OnDisassociateKeys(ICollection<InstanceKey> keys) => throw Fx.AssertAndThrow("WorkflowApplication is sealed with CanUseKeys as false, so WorkflowInstance should not call OnDisassociateKeys.");

        /// <summary>
        /// Called when [end associate keys].
        /// </summary>
        /// <param name="result">The result.</param>
        protected internal override void OnEndAssociateKeys(IAsyncResult result) => throw Fx.AssertAndThrow("WorkflowApplication is sealed with CanUseKeys as false, so WorkflowInstance should not call OnEndAssociateKeys.");

        /// <summary>
        /// Called when [end persist].
        /// </summary>
        /// <param name="result">The result.</param>
        protected internal override void OnEndPersist(IAsyncResult result) => this.EndInternalPersist(result);

        /// <summary>
        /// Called when [end resume bookmark].
        /// </summary>
        /// <param name="result">The result.</param>
        /// <returns>BookmarkResumptionResult.</returns>
        protected internal override BookmarkResumptionResult OnEndResumeBookmark(IAsyncResult result) => ResumeBookmarkAsyncResult.End(result);

        /// <summary>
        /// Called when [request abort].
        /// </summary>
        /// <param name="reason">The reason.</param>
        protected internal override void OnRequestAbort(Exception reason) => this.AbortInstance(reason, false);

        /// <summary>
        /// Called when [notify paused].
        /// </summary>
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

        /// <summary>
        /// Called when [notify unhandled exception].
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="exceptionSource">The exception source.</param>
        /// <param name="exceptionSourceInstanceId">The exception source instance identifier.</param>
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

        /// <summary>
        /// Completes the persistence context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="scope">The scope.</param>
        /// <param name="success">if set to <c>true</c> [success].</param>
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

        /// <summary>
        /// Creates the instance.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <param name="inputs">The inputs.</param>
        /// <param name="extensions">The extensions.</param>
        /// <param name="syncContext">The synchronize context.</param>
        /// <param name="invokeCompletedCallback">The invoke completed callback.</param>
        /// <returns>WorkflowApplication.</returns>
        /// <remarks>
        /// called on the Invoke path, this will go away when WorkflowInvoker implements
        /// WorkflowInstance directly
        /// </remarks>
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

        /// <summary>
        /// Events the frame.
        /// </summary>
        /// <param name="result">The result.</param>
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

        /// <summary>
        /// Executes the instance command with temporary handle.
        /// </summary>
        /// <param name="instanceStore">The instance store.</param>
        /// <param name="command">The command.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns>InstanceView.</returns>
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

        /// <summary>
        /// Extracts the state of the runtime.
        /// </summary>
        /// <param name="values">The values.</param>
        /// <param name="instanceId">The instance identifier.</param>
        /// <returns>ActivityExecutor.</returns>
        /// <exception cref="InstancePersistenceException"></exception>
        private static ActivityExecutor ExtractRuntimeState(IDictionary<XName, InstanceValue> values, Guid instanceId)
        {
            if (values.TryGetValue(WorkflowNamespace.Workflow, out var value) && value.Value is ActivityExecutor result)
            {
                return result;
            }

            throw FxTrace.Exception.AsError(new InstancePersistenceException(SR.WorkflowInstanceNotFoundInStore(instanceId)));
        }

        /// <summary>
        /// Gets the create owner command.
        /// </summary>
        /// <param name="definitionIdentity">The definition identity.</param>
        /// <param name="identityFilter">The identity filter.</param>
        /// <returns>CreateWorkflowOwnerWithIdentityCommand.</returns>
        /// <exception cref="ArgumentOutOfRangeException">identityFilter</exception>
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

        /// <summary>
        /// Initializes the persistence context.
        /// </summary>
        /// <param name="isTransactionRequired">if set to <c>true</c> [is transaction required].</param>
        /// <param name="timeoutHelper">The timeout helper.</param>
        /// <param name="context">The context.</param>
        /// <param name="scope">The scope.</param>
        private static void InitializePersistenceContext(bool isTransactionRequired, TimeoutHelper timeoutHelper,
            out WorkflowPersistenceContext context, out TransactionScope scope)
        {
            context = new WorkflowPersistenceContext(isTransactionRequired, timeoutHelper.OriginalTimeout);
            scope = TransactionHelper.CreateTransactionScope(context.PublicTransaction);
        }

        /// <summary>
        /// Loads the core.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <param name="loadAny">if set to <c>true</c> [load any].</param>
        /// <param name="persistenceManager">The persistence manager.</param>
        /// <returns>WorkflowApplicationInstance.</returns>
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

        /// <summary>
        /// Loads the values.
        /// </summary>
        /// <param name="persistenceManager">The persistence manager.</param>
        /// <param name="timeoutHelper">The timeout helper.</param>
        /// <param name="loadAny">if set to <c>true</c> [load any].</param>
        /// <returns>IDictionary&lt;XName, InstanceValue&gt;.</returns>
        /// <exception cref="InstanceNotReadyException"></exception>
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

        /// <summary>
        /// Called when [wait asynchronous complete].
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="exception">The exception.</param>
        private static void OnWaitAsyncComplete(object state, TimeoutException exception)
        {
            var data = (WaitForTurnData)state;

            if (!data.Instance.Remove(data.Operation))
            {
                exception = null;
            }

            data.Callback(data.State, exception);
        }

        /// <summary>
        /// Runs the instance.
        /// </summary>
        /// <param name="instance">The instance.</param>
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

        /// <summary>
        /// Starts the invoke.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <param name="inputs">The inputs.</param>
        /// <param name="extensions">The extensions.</param>
        /// <param name="syncContext">The synchronize context.</param>
        /// <param name="invokeCompletedCallback">The invoke completed callback.</param>
        /// <param name="invokeContext">The invoke context.</param>
        /// <returns>WorkflowApplication.</returns>
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

        /// <summary>
        /// Unlocks the instance.
        /// </summary>
        /// <param name="persistenceManager">The persistence manager.</param>
        /// <param name="timeoutHelper">The timeout helper.</param>
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

        /// <summary>
        /// Aborts the specified reason.
        /// </summary>
        /// <param name="reason">The reason.</param>
        /// <param name="innerException">The inner exception.</param>
        private void Abort(string reason, Exception innerException)
        {
            // This is pretty loose check, but it is okay if we go down the abort path multiple times
            if (this.state != WorkflowApplicationState.Aborted)
            {
                this.AbortInstance(new WorkflowApplicationAbortedException(reason, innerException), false);
            }
        }

        /// <summary>
        /// Aborts the due to exception.
        /// </summary>
        /// <param name="e">The e.</param>
        private void AbortDueToException(Exception e)
        {
            if (e is InstanceUpdateException)
            {
                this.Abort(SR.AbortingDueToDynamicUpdateFailure, e);
            }
            else
            if (e is VersionMismatchException)
            {
                this.Abort(SR.AbortingDueToVersionMismatch, e);
            }
            else
            {
                this.Abort(SR.AbortingDueToLoadFailure);
            }
        }

        /// <summary>
        /// Aborts the instance.
        /// </summary>
        /// <param name="reason">The reason.</param>
        /// <param name="isWorkflowThread">if set to <c>true</c> [is workflow thread].</param>
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

        /// <summary>
        /// Aborts the persistence.
        /// </summary>
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

        /// <summary>
        /// Adds to pending.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="push">if set to <c>true</c> [push].</param>
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

        /// <summary>
        /// Ares the bookmarks invalid.
        /// </summary>
        /// <param name="result">The result.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
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

        /// <summary>
        /// Begins the internal persist.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="isInternalPersist">if set to <c>true</c> [is internal persist].</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        private IAsyncResult BeginInternalPersist(PersistenceOperation operation, TimeSpan timeout, bool isInternalPersist, AsyncCallback callback, object state) => new UnloadOrPersistAsyncResult(this, timeout, operation, true, isInternalPersist, callback, state);

        /// <summary>
        /// Begins the internal run.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <param name="isUserRun">if set to <c>true</c> [is user run].</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        private IAsyncResult BeginInternalRun(TimeSpan timeout, bool isUserRun, AsyncCallback callback, object state)
        {
            this.ThrowIfHandlerThread();

            TimeoutHelper.ThrowIfNegativeArgument(timeout);

            return RunAsyncResult.Create(this, isUserRun, timeout, callback, state);
        }

        /// <summary>
        /// called from the sync and async paths
        /// </summary>
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

        /// <summary>
        /// Creates the persistence manager.
        /// </summary>
        private void CreatePersistenceManager()
        {
            var newManager = new PersistenceManager(this.InstanceStore, this.GetInstanceMetadata(), this.instanceId);
            this.SetPersistenceManager(newManager);
        }

        /// <summary>
        /// Decrements the pending unenqueud.
        /// </summary>
        private void DecrementPendingUnenqueud()
        {
            lock (this.pendingOperations)
            {
                this.pendingUnenqueued--;
            }
        }

        /// <summary>
        /// Ends the internal persist.
        /// </summary>
        /// <param name="result">The result.</param>
        private void EndInternalPersist(IAsyncResult result) => UnloadOrPersistAsyncResult.End(result);

        /// <summary>
        /// Enqueues the specified operation.
        /// </summary>
        /// <param name="operation">The operation.</param>
        private void Enqueue(InstanceOperation operation) => this.Enqueue(operation, false);

        /// <summary>
        /// Enqueues the specified operation.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="push">if set to <c>true</c> [push].</param>
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

        /// <summary>
        /// Ensures the initialized.
        /// </summary>
        /// <remarks>
        /// assumes that we're called under the pendingOperations lock
        /// </remarks>
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

        /// <summary>
        /// Finds the operation.
        /// </summary>
        /// <returns>InstanceOperation.</returns>
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

        /// <summary>
        /// Forces the notify operation complete.
        /// </summary>
        /// <remarks>
        /// For when we know that the operation is non-null and notified (like in async paths)
        /// </remarks>
        private void ForceNotifyOperationComplete() => this.OnNotifyPaused();

        /// <summary>
        /// Gets the instance metadata.
        /// </summary>
        /// <returns>IDictionary&lt;XName, InstanceValue&gt;.</returns>
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

        /// <summary>
        /// Increments the pending unenqueud.
        /// </summary>
        private void IncrementPendingUnenqueud()
        {
            lock (this.pendingOperations)
            {
                this.pendingUnenqueued++;
            }
        }

        /// <summary>
        /// Internals the run.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <param name="isUserRun">if set to <c>true</c> [is user run].</param>
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

        /// <summary>
        /// Determines whether [is load transaction required].
        /// </summary>
        /// <returns><c>true</c> if [is load transaction required]; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// shared by Load/BeginLoad
        /// </remarks>
        private bool IsLoadTransactionRequired() => base.GetExtensions<IPersistencePipelineModule>().Any(module => module.IsLoadTransactionRequired);

        /// <summary>
        /// Loads the core.
        /// </summary>
        /// <param name="updateMap">The update map.</param>
        /// <param name="timeoutHelper">The timeout helper.</param>
        /// <param name="loadAny">if set to <c>true</c> [load any].</param>
        /// <param name="values">The values.</param>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        private void LoadCore(DynamicUpdateMap updateMap, TimeoutHelper timeoutHelper, bool loadAny, IDictionary<XName, InstanceValue> values = null)
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
                    base.Initialize(deserializedRuntimeState, updateMap);
                    if (updateMap != null)
                    {
                        this.UpdateInstanceMetadata();
                    }
                }
                catch (InstanceUpdateException e)
                {
                    abortReasonInnerException = e;
                    throw;
                }
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

        /// <summary>
        /// Marks the unloaded.
        /// </summary>
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

        /// <summary>
        /// Notifies the operation complete.
        /// </summary>
        /// <param name="operation">The operation.</param>
        private void NotifyOperationComplete(InstanceOperation operation)
        {
            if (operation != null && operation.Notified)
            {
                this.OnNotifyPaused();
            }
        }

        /// <summary>
        /// Called when [abort tracking complete].
        /// </summary>
        /// <param name="result">The result.</param>
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

        /// <summary>
        /// Called when [abort wait complete].
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="exception">The exception.</param>
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

        /// <summary>
        /// Persists the core.
        /// </summary>
        /// <param name="timeoutHelper">The timeout helper.</param>
        /// <param name="operation">The operation.</param>
        /// <exception cref="OperationCanceledException"></exception>
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

        /// <summary>
        /// Processes the instance values.
        /// </summary>
        /// <param name="values">The values.</param>
        /// <param name="deserializedRuntimeState">State of the deserialized runtime.</param>
        /// <returns>PersistencePipeline.</returns>
        /// <remarks>
        /// shared by Load/BeginLoad
        /// </remarks>
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

        /// <summary>
        /// Raises the aborted.
        /// </summary>
        /// <param name="reason">The reason.</param>
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

        /// <summary>
        /// Raises the idle event.
        /// </summary>
        /// <returns><c>true</c> if successful, <c>false</c> otherwise.</returns>
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

        /// <summary>
        /// Removes the specified operation.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool Remove(InstanceOperation operation)
        {
            lock (this.pendingOperations)
            {
                return this.pendingOperations.Remove(operation);
            }
        }

        /// <summary>
        /// Resumes the bookmark core.
        /// </summary>
        /// <param name="bookmark">The bookmark.</param>
        /// <param name="value">The value.</param>
        /// <returns>BookmarkResumptionResult.</returns>
        private BookmarkResumptionResult ResumeBookmarkCore(Bookmark bookmark, object value)
        {
            var result = this.Controller.ScheduleBookmarkResumption(bookmark, value);

            if (result == BookmarkResumptionResult.Success)
            {
                this.RunCore();
            }

            return result;
        }

        /// <summary>
        /// Runs the core.
        /// </summary>
        private void RunCore()
        {
            if (!this.hasCalledRun)
            {
                this.hasCalledRun = true;
            }

            this.state = WorkflowApplicationState.Runnable;
        }

        /// <summary>
        /// Schedules the track and raise aborted.
        /// </summary>
        /// <param name="reason">The reason.</param>
        private void ScheduleTrackAndRaiseAborted(Exception reason)
        {
            if (this.Controller.HasPendingTrackingRecords || this.Aborted != null)
            {
                ActionItem.Schedule(new Action<object>(this.TrackAndRaiseAborted), reason);
            }
        }

        /// <summary>
        /// Sets the persistence manager.
        /// </summary>
        /// <param name="newManager">The new manager.</param>
        /// <remarks>
        /// shared by Load(WorkflowApplicationInstance)/BeginLoad*
        /// </remarks>
        private void SetPersistenceManager(PersistenceManager newManager)
        {
            Fx.Assert(this.persistenceManager == null, "SetPersistenceManager should only be called once");

            // first register our extensions since we'll need them to construct the pipeline
            base.RegisterExtensionManager(this.extensions);
            this.persistenceManager = newManager;
        }

        /// <summary>
        /// Shoulds the raise complete.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool ShouldRaiseComplete(WorkflowInstanceState state) => state == WorkflowInstanceState.Complete && !this.hasRaisedCompleted;

        /// <summary>
        /// Terminates the core.
        /// </summary>
        /// <param name="reason">The reason.</param>
        private void TerminateCore(Exception reason) => this.Controller.Terminate(reason);

        /// <summary>
        /// Throws if aborted.
        /// </summary>
        /// <exception cref="WorkflowApplicationAbortedException"></exception>
        private void ThrowIfAborted()
        {
            if (this.state == WorkflowApplicationState.Aborted)
            {
                throw FxTrace.Exception.AsError(new WorkflowApplicationAbortedException(SR.WorkflowApplicationAborted(this.Id), this.Id));
            }
        }

        /// <summary>
        /// Throws if handler thread.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void ThrowIfHandlerThread()
        {
            if (this.IsHandlerThread)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotPerformOperationFromHandlerThread));
            }
        }

        /// <summary>
        /// Throws if multicast.
        /// </summary>
        /// <param name="value">The value.</param>
        private void ThrowIfMulticast(Delegate value)
        {
            if (value != null && value.GetInvocationList().Length > 1)
            {
                throw FxTrace.Exception.Argument(nameof(value), SR.OnlySingleCastDelegatesAllowed);
            }
        }

        /// <summary>
        /// Throws if no instance store.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void ThrowIfNoInstanceStore()
        {
            if (!this.HasPersistenceProvider)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.InstanceStoreRequiredToPersist));
            }
        }

        /// <summary>
        /// Throws if terminated or completed.
        /// </summary>
        /// <exception cref="WorkflowApplicationTerminatedException"></exception>
        /// <exception cref="WorkflowApplicationCompletedException"></exception>
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

        /// <summary>
        /// Throws if unloaded.
        /// </summary>
        /// <exception cref="WorkflowApplicationUnloadedException"></exception>
        private void ThrowIfUnloaded()
        {
            if (this.state == WorkflowApplicationState.Unloaded)
            {
                throw FxTrace.Exception.AsError(new WorkflowApplicationUnloadedException(SR.WorkflowApplicationUnloaded(this.Id), this.Id));
            }
        }

        /// <summary>
        /// Tracks the and raise aborted.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <remarks>
        /// This is only ever called from an appropriate thread (not the thread that called abort
        /// unless it was an internal abort). This method is called without the lock. We still
        /// provide single threaded guarantees to the Controller because:
        /// * No other call can ever enter the executor again once the state has switched to Aborted
        /// * If this was an internal abort then the thread was fast pathing its way out of the
        /// runtime and won't conflict
        /// </remarks>
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

        /// <summary>
        /// Tracks the persistence.
        /// </summary>
        /// <param name="operation">The operation.</param>
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

        /// <summary>
        /// Updates the instance metadata.
        /// </summary>
        private void UpdateInstanceMetadata() =>
            // Update the metadata to reflect the new identity after a Dynamic Update
            this.persistenceManager.SetMutablemetadata(new Dictionary<XName, InstanceValue>
            {
                { Workflow45Namespace.DefinitionIdentity, new InstanceValue(this.DefinitionIdentity, InstanceValueOptions.Optional) }
            });

        /// <summary>
        /// Validates the state for cancel.
        /// </summary>
        private void ValidateStateForCancel() =>
            // WorkflowInstanceException validations
            this.ThrowIfAborted();

        /// <summary>
        /// Validates the state for get all bookmarks.
        /// </summary>
        private void ValidateStateForGetAllBookmarks()
        {
            // WorkflowInstanceException validations
            this.ThrowIfAborted();
            this.ThrowIfTerminatedOrCompleted();
            this.ThrowIfUnloaded();
        }

        /// <summary>
        /// Validates the state for load.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void ValidateStateForLoad()
        {
            this.ThrowIfAborted();
            this.ThrowIfReadOnly(); // only allow a single Load() or Run()
            if (this.instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowApplicationAlreadyHasId));
            }
        }

        /// <summary>
        /// We only validate that we aren't aborted and no-op otherwise. This is because the//
        /// scenario for calling cancel is for it to be a best attempt from an unknown thread.// The
        /// less it throws the easier it is to author a host.
        /// </summary>
        private void ValidateStateForPersist()
        {
            // WorkflowInstanceException validations
            this.ThrowIfAborted();
            this.ThrowIfTerminatedOrCompleted();
            this.ThrowIfUnloaded();

            // Other validations
            this.ThrowIfNoInstanceStore();
        }

        /// <summary>
        /// Validates the state for run.
        /// </summary>
        private void ValidateStateForRun()
        {
            // WorkflowInstanceException validations
            this.ThrowIfAborted();
            this.ThrowIfTerminatedOrCompleted();
            this.ThrowIfUnloaded();
        }

        /// <summary>
        /// Validates the state for terminate.
        /// </summary>
        private void ValidateStateForTerminate()
        {
            // WorkflowInstanceException validations
            this.ThrowIfAborted();
            this.ThrowIfTerminatedOrCompleted();
            this.ThrowIfUnloaded();
        }

        /// <summary>
        /// Validates the state for unload.
        /// </summary>
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

        /// <summary>
        /// Waits for turn.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool WaitForTurn(InstanceOperation operation, TimeSpan timeout)
        {
            this.Enqueue(operation);
            return this.WaitForTurnNoEnqueue(operation, timeout);
        }

        /// <summary>
        /// Waits for turn asynchronous.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool WaitForTurnAsync(InstanceOperation operation, TimeSpan timeout, Action<object, TimeoutException> callback, object state) => this.WaitForTurnAsync(operation, false, timeout, callback, state);

        /// <summary>
        /// Waits for turn asynchronous.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="push">if set to <c>true</c> [push].</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool WaitForTurnAsync(InstanceOperation operation, bool push, TimeSpan timeout, Action<object, TimeoutException> callback, object state)
        {
            this.Enqueue(operation, push);

            return this.WaitForTurnNoEnqueueAsync(operation, timeout, callback, state);
        }

        /// <summary>
        /// Waits for turn no enqueue.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        /// <exception cref="TimeoutException"></exception>
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

        /// <summary>
        /// Waits for turn no enqueue asynchronous.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool WaitForTurnNoEnqueueAsync(InstanceOperation operation, TimeSpan timeout, Action<object, TimeoutException> callback, object state)
        {
            if (waitAsyncCompleteCallback == null)
            {
                waitAsyncCompleteCallback = new Action<object, TimeoutException>(OnWaitAsyncComplete);
            }
            return operation.WaitForTurnAsync(timeout, waitAsyncCompleteCallback, new WaitForTurnData(callback, state, operation, this));
        }
    }
}
