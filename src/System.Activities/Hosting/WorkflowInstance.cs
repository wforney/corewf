// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Hosting
{
    using CoreWf.Tracking;

    using System;
    using System.Activities.DynamicUpdate;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.DurableInstancing;
    using System.Activities.Tracking;
    using System.Activities.Validation;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading;

    /// <summary>
    /// The WorkflowInstance class.
    /// </summary>
    [Fx.Tag.XamlVisible(false)]
    public abstract class WorkflowInstance
    {
        private static readonly IDictionary<string, LocationInfo> EmptyMappedVariablesDictionary =
            new ReadOnlyDictionary<string, LocationInfo>(new Dictionary<string, LocationInfo>(0));

        private const int True = 1;
        private const int False = 0;
        private WorkflowInstanceControl controller;
        private TrackingProvider? trackingProvider;
        private SynchronizationContext? syncContext;
        private LocationReferenceEnvironment? hostEnvironment;
        private ActivityExecutor? executor;
        private int isPerformingOperation;
        private WorkflowInstanceExtensionCollection? extensions;

        /// <summary>
        /// Tracking for one-time actions per in-memory instance        
        /// </summary>
        private bool hasTrackedResumed;

        private bool hasTrackedCompletion;
        private bool isAborted;
        private Exception? abortedException;

#if DEBUG
        private readonly Diagnostics.StackTrace? abortStack;
#endif

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowInstance"/> class.
        /// </summary>
        /// <param name="workflowDefinition">The workflow definition.</param>
        protected WorkflowInstance(Activity workflowDefinition)
            : this(workflowDefinition, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowInstance"/> class.
        /// </summary>
        /// <param name="workflowDefinition">The workflow definition.</param>
        /// <param name="definitionIdentity">The definition identity.</param>
        protected WorkflowInstance(Activity workflowDefinition, WorkflowIdentity? definitionIdentity)
        {
            this.WorkflowDefinition = workflowDefinition ?? throw FxTrace.Exception.ArgumentNull(nameof(workflowDefinition));
            this.DefinitionIdentity = definitionIdentity;
        }

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        public abstract Guid Id { get; }

        internal bool HasTrackingParticipant { get; private set; }

        internal bool HasTrackedStarted { get; private set; }

        internal bool HasPersistenceModule { get; private set; }

        /// <summary>
        /// Gets or sets the synchronization context.
        /// </summary>
        /// <value>The synchronization context.</value>
        public SynchronizationContext? SynchronizationContext
        {
            get => this.syncContext;
            set
            {
                this.ThrowIfReadOnly();
                this.syncContext = value;
            }
        }

        /// <summary>
        /// Gets or sets the host environment.
        /// </summary>
        /// <value>The host environment.</value>
        public LocationReferenceEnvironment? HostEnvironment
        {
            get => this.hostEnvironment;
            set
            {
                this.ThrowIfReadOnly();
                this.hostEnvironment = value;
            }
        }

        /// <summary>
        /// Gets the workflow definition.
        /// </summary>
        /// <value>The workflow definition.</value>
        public Activity WorkflowDefinition { get; private set; }

        /// <summary>
        /// Gets the definition identity.
        /// </summary>
        /// <value>The definition identity.</value>
        public WorkflowIdentity? DefinitionIdentity { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance is read only.
        /// </summary>
        /// <value><c>true</c> if this instance is read only; otherwise, <c>false</c>.</value>
        protected bool IsReadOnly { get; private set; }

        /// <summary>
        /// Gets a value indicating whether [supports instance keys].
        /// </summary>
        /// <value><c>true</c> if [supports instance keys]; otherwise, <c>false</c>.</value>
        protected internal abstract bool SupportsInstanceKeys { get; }

        /// <summary>
        /// Gets the tracking provider.
        /// </summary>
        /// <value>The tracking provider.</value>
        /// <remarks>
        /// this is going away        
        /// </remarks>
        internal TrackingProvider? TrackingProvider
        {
            get
            {
                Fx.Assert(this.HasTrackingParticipant, "we should only be called if we have a tracking participant");
                return this.trackingProvider;
            }
        }

        /// <summary>
        /// Gets the controller.
        /// </summary>
        /// <value>The controller.</value>
        /// <exception cref="InvalidOperationException"></exception>
        [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations", Justification = "<Pending>")]
        protected WorkflowInstanceControl Controller
        {
            get
            {
                if (!this.IsReadOnly)
                {
                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.ControllerInvalidBeforeInitialize));
                }

                return this.controller;
            }
        }

        /// <summary>
        /// Gets the extension.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>T.</returns>
        /// <remarks>
        /// host-facing access to our cascading ExtensionManager resolution        
        /// </remarks>
        protected internal T GetExtension<T>() where T : class =>
            this.extensions == null ? default : this.extensions.Find<T>();

        /// <summary>
        /// Gets the extensions.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>System.Collections.Generic.IEnumerable&lt;T&gt;.</returns>
        protected internal IEnumerable<T?> GetExtensions<T>() where T : class
        {
            if (this.extensions != null)
            {
                return this.extensions.FindAll<T>();
            }
            else
            {
                return Array.Empty<T>();
            }
        }

        /// <summary>
        /// Registers the extension manager.
        /// </summary>
        /// <param name="extensionManager">The extension manager.</param>
        /// <remarks>
        /// locks down the given extensions manager and runs cache metadata on the workflow definition        
        /// </remarks>
        protected void RegisterExtensionManager(WorkflowInstanceExtensionManager extensionManager)
        {
            this.ValidateWorkflow(extensionManager);
            this.extensions = WorkflowInstanceExtensionManager.CreateInstanceExtensions(this.WorkflowDefinition, extensionManager);
            if (this.extensions != null)
            {
                this.HasPersistenceModule = this.extensions.HasPersistenceModule;
            }
        }

        /// <summary>
        /// Disposes the extensions.
        /// </summary>
        /// <remarks>
        /// dispose the extensions that implement IDisposable        
        /// </remarks>
        protected void DisposeExtensions()
        {
            if (this.extensions != null)
            {
                this.extensions.Dispose();
                this.extensions = null;
            }
        }

        /// <summary>
        /// Gets the activities blocking update.
        /// </summary>
        /// <param name="deserializedRuntimeState">State of the deserialized runtime.</param>
        /// <param name="updateMap">The update map.</param>
        /// <returns>IList&lt;ActivityBlockingUpdate&gt;.</returns>
        protected static IList<ActivityBlockingUpdate> GetActivitiesBlockingUpdate(object deserializedRuntimeState, DynamicUpdateMap updateMap)
        {
            if (!(deserializedRuntimeState is ActivityExecutor executor))
            {
                throw FxTrace.Exception.Argument(nameof(deserializedRuntimeState), SR.InvalidRuntimeState);
            }

            if (updateMap == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(updateMap));
            }

            var rootMap = updateMap;
            if (updateMap.IsForImplementation)
            {
                rootMap = updateMap.AsRootMap();
            }

            var result = executor.GetActivitiesBlockingUpdate(rootMap);
            if (result == null)
            {
                result = new List<ActivityBlockingUpdate>();
            }

            return result;
        }

        /// <summary>
        /// Initializes the specified workflow argument values.
        /// </summary>
        /// <param name="workflowArgumentValues">The workflow argument values.</param>
        /// <param name="workflowExecutionProperties">The workflow execution properties.</param>
        /// <remarks>
        /// used for Create scenarios where you are providing root information        
        /// </remarks>
        protected void Initialize(IDictionary<string, object> workflowArgumentValues, IList<Handle> workflowExecutionProperties)
        {
            this.ThrowIfAborted();
            this.ThrowIfReadOnly();
            this.executor = new ActivityExecutor(this);

            this.EnsureDefinitionReady();
            // workflowArgumentValues signals whether we are a new or loaded instance, so we can't
            // pass in null. workflowExecutionProperties is allowed to be null
            this.InitializeCore(workflowArgumentValues ?? ActivityUtilities.EmptyParameters, workflowExecutionProperties);
        }

        /// <summary>
        /// Initializes the specified deserialized runtime state.
        /// </summary>
        /// <param name="deserializedRuntimeState">State of the deserialized runtime.</param>
        /// <remarks>
        /// used for Load scenarios where you are rehydrating a WorkflowInstance        
        /// </remarks>
        protected void Initialize(object deserializedRuntimeState)
        {
            this.Initialize(deserializedRuntimeState, null);
        }

        /// <summary>
        /// Initializes the specified deserialized runtime state.
        /// </summary>
        /// <param name="deserializedRuntimeState">State of the deserialized runtime.</param>
        /// <param name="updateMap">The update map.</param>
        /// <exception cref="InstanceUpdateException"></exception>
        protected void Initialize(object deserializedRuntimeState, DynamicUpdateMap updateMap)
        {
            this.ThrowIfAborted();
            this.ThrowIfReadOnly();
            this.executor = deserializedRuntimeState as ActivityExecutor;

            if (this.executor == null)
            {
                throw FxTrace.Exception.Argument(nameof(deserializedRuntimeState), SR.InvalidRuntimeState);
            }

            this.executor.ThrowIfNonSerializable();

            this.EnsureDefinitionReady();

            var originalDefinitionIdentity = this.executor.WorkflowIdentity;
            var success = false;
            Collection<ActivityBlockingUpdate>? updateErrors = null;
            try
            {
                if (updateMap != null)
                {
                    // check if map is for implementaiton,
                    if (updateMap.IsForImplementation)
                    {
                        // if so, the definition root must be an activity with no public/imported
                        // children and no public/imported delegates.
                        if (DynamicUpdateMap.CanUseImplementationMapAsRoot(this.WorkflowDefinition))
                        {
                            updateMap = updateMap.AsRootMap();
                        }
                        else
                        {
                            throw FxTrace.Exception.AsError(new InstanceUpdateException(SR.InvalidImplementationAsWorkflowRoot));
                        }
                    }

                    updateMap.ThrowIfInvalid(this.WorkflowDefinition);

                    this.executor.WorkflowIdentity = this.DefinitionIdentity;

                    this.executor.UpdateInstancePhase1(updateMap, this.WorkflowDefinition, ref updateErrors);
                    this.ThrowIfDynamicUpdateErrorExists(updateErrors);
                }

                this.InitializeCore(null, null);

                if (updateMap != null)
                {
                    this.executor.UpdateInstancePhase2(updateMap, ref updateErrors);
                    this.ThrowIfDynamicUpdateErrorExists(updateErrors);
                    // Track that dynamic update is successful
                    if (this.Controller.TrackingEnabled)
                    {
                        this.Controller.Track(new WorkflowInstanceUpdatedRecord(this.Id, this.WorkflowDefinition.DisplayName, originalDefinitionIdentity, this.executor.WorkflowIdentity));
                    }
                }

                success = true;
            }
            catch (InstanceUpdateException updateException)
            {
                // Can't track through the controller because initialization failed
                if (this.HasTrackingParticipant && this.TrackingProvider.ShouldTrackWorkflowInstanceRecords)
                {
                    var blockingActivities = updateException.BlockingActivities;
                    if (blockingActivities.Count == 0)
                    {
                        blockingActivities = new List<ActivityBlockingUpdate>
                        {
                            new ActivityBlockingUpdate(this.WorkflowDefinition, this.WorkflowDefinition.Id, updateException.Message)
                        }.AsReadOnly();
                    }
                    this.TrackingProvider.AddRecord(new WorkflowInstanceUpdatedRecord(this.Id, this.WorkflowDefinition.DisplayName, originalDefinitionIdentity, this.DefinitionIdentity, blockingActivities));
                }
                throw;
            }
            finally
            {
                if (updateMap != null && !success)
                {
                    this.executor.MakeNonSerializable();
                }
            }
        }

        private void ThrowIfDynamicUpdateErrorExists(Collection<ActivityBlockingUpdate>? updateErrors)
        {
            if (updateErrors != null && updateErrors.Count > 0)
            {
                // update error found exit early

                throw FxTrace.Exception.AsError(new InstanceUpdateException(updateErrors));
            }
        }

        //protected void Initialize(object deserializedRuntimeState)
        //{
        //    this.ThrowIfAborted();
        //    this.ThrowIfReadOnly();
        //    this.executor = deserializedRuntimeState as ActivityExecutor;

        // if (this.executor == null) { throw
        // FxTrace.Exception.Argument(nameof(deserializedRuntimeState), SR.InvalidRuntimeState); } this.executor.ThrowIfNonSerializable();

        // this.EnsureDefinitionReady();

        //    this.InitializeCore(null, null);
        //}

        private void ValidateWorkflow(WorkflowInstanceExtensionManager extensionManager)
        {
            if (!this.WorkflowDefinition.IsRuntimeReady)
            {
                var localEnvironment = this.hostEnvironment;
                if (localEnvironment == null)
                {
                    LocationReferenceEnvironment parentEnvironment = null;
                    if (extensionManager != null && extensionManager.SymbolResolver != null)
                    {
                        parentEnvironment = extensionManager.SymbolResolver.AsLocationReferenceEnvironment();
                    }
                    localEnvironment = new ActivityLocationReferenceEnvironment(parentEnvironment);
                }
                IList<ValidationError> validationErrors = null;
                ActivityUtilities.CacheRootMetadata(this.WorkflowDefinition, localEnvironment, ProcessActivityTreeOptions.FullCachingOptions, null, ref validationErrors);
                ActivityValidationServices.ThrowIfViolationsExist(validationErrors);
            }
        }

        private void EnsureDefinitionReady()
        {
            if (this.extensions != null)
            {
                this.extensions.Initialize();
                if (this.extensions.HasTrackingParticipant)
                {
                    this.HasTrackingParticipant = true;
                    if (this.trackingProvider == null)
                    {
                        this.trackingProvider = new TrackingProvider(this.WorkflowDefinition);
                    }
                    else
                    {
                        // TrackingProvider could be non-null if an earlier initialization attempt
                        // failed. This happens when WorkflowApplication calls Abort after a load
                        // failure. In this case we want to preserve any pending tracking records
                        // (e.g. DU failure).
                        this.trackingProvider.ClearParticipants();
                    }
                    foreach (var trackingParticipant in this.GetExtensions<TrackingParticipant>())
                    {
                        this.trackingProvider.AddParticipant(trackingParticipant);
                    }
                }
            }
            else
            {
                // need to ensure the workflow has been validated since the host isn't using
                // extensions (and so didn't register anything)
                this.ValidateWorkflow(null);
            }
        }

        private void InitializeCore(IDictionary<string, object> workflowArgumentValues, IList<Handle> workflowExecutionProperties)
        {
            Fx.Assert(this.WorkflowDefinition.IsRuntimeReady, "EnsureDefinitionReady should have been called");
            Fx.Assert(this.executor != null, "at this point, we better have an executor");

            // Do Argument validation for root activities
            this.WorkflowDefinition.HasBeenAssociatedWithAnInstance = true;

            if (workflowArgumentValues != null)
            {
                var actualInputs = workflowArgumentValues;

                if (object.ReferenceEquals(actualInputs, ActivityUtilities.EmptyParameters))
                {
                    actualInputs = null;
                }

                if (this.WorkflowDefinition.RuntimeArguments.Count > 0 || (actualInputs != null && actualInputs.Count > 0))
                {
                    ActivityValidationServices.ValidateRootInputs(this.WorkflowDefinition, actualInputs);
                }

                this.executor.ScheduleRootActivity(this.WorkflowDefinition, actualInputs, workflowExecutionProperties);
            }
            else
            {
                this.executor.OnDeserialized(this.WorkflowDefinition, this);
            }

            this.executor.Open(this.SynchronizationContext);
            this.controller = new WorkflowInstanceControl(this, this.executor);
            this.IsReadOnly = true;

            if (this.extensions != null && this.extensions.HasWorkflowInstanceExtensions)
            {
                var proxy = new WorkflowInstanceProxy(this);

                for (var i = 0; i < this.extensions.WorkflowInstanceExtensions.Count; i++)
                {
                    var extension = this.extensions.WorkflowInstanceExtensions[i];
                    extension.SetInstance(proxy);
                }
            }
        }

        protected void ThrowIfReadOnly()
        {
            if (this.IsReadOnly)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowInstanceIsReadOnly(this.Id)));
            }
        }

        protected internal abstract IAsyncResult OnBeginResumeBookmark(Bookmark bookmark, object value, TimeSpan timeout, AsyncCallback callback, object state);

        protected internal abstract BookmarkResumptionResult OnEndResumeBookmark(IAsyncResult result);

        protected internal abstract IAsyncResult OnBeginPersist(AsyncCallback callback, object state);

        protected internal abstract void OnEndPersist(IAsyncResult result);

        protected internal abstract void OnDisassociateKeys(ICollection<InstanceKey> keys);

        protected internal abstract IAsyncResult OnBeginAssociateKeys(ICollection<InstanceKey> keys, AsyncCallback callback, object state);

        protected internal abstract void OnEndAssociateKeys(IAsyncResult result);

        internal IAsyncResult BeginFlushTrackingRecordsInternal(AsyncCallback callback, object state) => this.OnBeginFlushTrackingRecords(callback, state);

        internal void EndFlushTrackingRecordsInternal(IAsyncResult result) => this.OnEndFlushTrackingRecords(result);

        protected void FlushTrackingRecords(TimeSpan timeout)
        {
            if (this.HasTrackingParticipant)
            {
                this.TrackingProvider.FlushPendingRecords(timeout);
            }
        }

        protected IAsyncResult BeginFlushTrackingRecords(TimeSpan timeout, AsyncCallback callback, object state)
        {
            if (this.HasTrackingParticipant)
            {
                return this.TrackingProvider.BeginFlushPendingRecords(timeout, callback, state);
            }
            else
            {
                return new CompletedAsyncResult(callback, state);
            }
        }

        protected void EndFlushTrackingRecords(IAsyncResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (this.HasTrackingParticipant)
            {
                this.TrackingProvider.EndFlushPendingRecords(result);
            }
            else
            {
                CompletedAsyncResult.End(result);
            }
        }

        protected virtual IAsyncResult OnBeginFlushTrackingRecords(AsyncCallback callback, object state) => this.Controller.BeginFlushTrackingRecords(ActivityDefaults.TrackingTimeout, callback, state);

        protected virtual void OnEndFlushTrackingRecords(IAsyncResult result) => this.Controller.EndFlushTrackingRecords(result);

        internal void NotifyPaused()
        {
            if (this.executor.State != ActivityInstanceState.Executing)
            {
                this.TrackCompletion();
            }

            this.OnNotifyPaused();
        }

        protected abstract void OnNotifyPaused();

        internal void NotifyUnhandledException(Exception exception, Activity source, string sourceInstanceId)
        {
            if (this.controller.TrackingEnabled)
            {
                var faultSourceInfo = new ActivityInfo(source.DisplayName, source.Id, sourceInstanceId, source.GetType().FullName);
                this.controller.Track(new WorkflowInstanceUnhandledExceptionRecord(this.Id, this.WorkflowDefinition.DisplayName, faultSourceInfo, exception, this.DefinitionIdentity));
            }

            this.OnNotifyUnhandledException(exception, source, sourceInstanceId);
        }

        protected abstract void OnNotifyUnhandledException(Exception exception, Activity source, string sourceInstanceId);

        protected internal abstract void OnRequestAbort(Exception reason);

        internal void OnDeserialized(bool hasTrackedStarted) => this.HasTrackedStarted = hasTrackedStarted;

        private void StartOperation(ref bool resetRequired)
        {
            this.StartReadOnlyOperation(ref resetRequired);

            // isRunning can only flip to true by an operation and therefore we don't have to worry
            // about this changing under us
            if (this.executor.IsRunning)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.RuntimeRunning));
            }
        }

        private void StartReadOnlyOperation(ref bool resetRequired)
        {
            bool wasPerformingOperation;
            try
            {
            }
            finally
            {
                wasPerformingOperation = Interlocked.CompareExchange(ref this.isPerformingOperation, True, False) == True;

                if (!wasPerformingOperation)
                {
                    resetRequired = true;
                }
            }

            if (wasPerformingOperation)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.RuntimeOperationInProgress));
            }
        }

        private void FinishOperation(ref bool resetRequired)
        {
            if (resetRequired)
            {
                this.isPerformingOperation = False;
            }
        }

        internal void Abort(Exception reason)
        {
            if (!this.isAborted)
            {
                this.isAborted = true;
                if (reason != null)
                {
                    this.abortedException = reason;
                }

                if (this.extensions != null)
                {
                    this.extensions.Cancel();
                }

                if (this.controller.TrackingEnabled)
                {
                    // During abort we only track this one record
                    if (reason != null)
                    {
                        var message = reason.Message;
                        if (reason.InnerException != null)
                        {
                            message = SR.WorkflowAbortedReason(reason.Message, reason.InnerException.Message);
                        }
                        this.controller.Track(new WorkflowInstanceAbortedRecord(this.Id, this.WorkflowDefinition.DisplayName, message, this.DefinitionIdentity));
                    }
                }
#if DEBUG && NET45
                if (!Fx.FastDebug)
                {
                    if (reason != null)
                    {
                        reason.ToString();
                    }
                    this.abortStack = new StackTrace();
                }
#endif
            }
        }

        private void ValidatePrepareForSerialization()
        {
            this.ThrowIfAborted();
            if (!this.Controller.IsPersistable)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.PrepareForSerializationRequiresPersistability));
            }
        }

        private void ValidateScheduleResumeBookmark()
        {
            this.ThrowIfAborted();
            this.ThrowIfNotIdle();
        }

        private void ValidateGetBookmarks() => this.ThrowIfAborted();

        private void ValidateGetMappedVariables() => this.ThrowIfAborted();

        private void ValidatePauseWhenPersistable()
        {
            this.ThrowIfAborted();
            if (this.Controller.IsPersistable)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.PauseWhenPersistableInvalidIfPersistable));
            }
        }

        private void Terminate(Exception reason)
        {
            // validate we're in an ok state
            this.ThrowIfAborted();

            // terminate the runtime
            this.executor.Terminate(reason);

            // and track if necessary
            this.TrackCompletion();
        }

        private void TrackCompletion()
        {
            if (this.controller.TrackingEnabled && !this.hasTrackedCompletion)
            {
                var completionState = this.executor.State;

                if (completionState == ActivityInstanceState.Faulted)
                {
                    Fx.Assert(this.executor.TerminationException != null, "must have a termination exception if we're faulted");
                    this.controller.Track(new WorkflowInstanceTerminatedRecord(this.Id, this.WorkflowDefinition.DisplayName, this.executor.TerminationException.Message, this.DefinitionIdentity));
                }
                else if (completionState == ActivityInstanceState.Closed)
                {
                    this.controller.Track(new WorkflowInstanceRecord(this.Id, this.WorkflowDefinition.DisplayName, WorkflowInstanceStates.Completed, this.DefinitionIdentity));
                }
                else
                {
                    Fx.AssertAndThrow(completionState == ActivityInstanceState.Canceled, "Cannot be executing a workflow instance when WorkflowState was completed.");
                    this.controller.Track(new WorkflowInstanceRecord(this.Id, this.WorkflowDefinition.DisplayName, WorkflowInstanceStates.Canceled, this.DefinitionIdentity));
                }
                this.hasTrackedCompletion = true;
            }
        }

        private void TrackResumed()
        {
            // track if necessary
            if (!this.hasTrackedResumed)
            {
                if (this.Controller.TrackingEnabled)
                {
                    if (!this.HasTrackedStarted)
                    {
                        this.TrackingProvider.AddRecord(new WorkflowInstanceRecord(this.Id, this.WorkflowDefinition.DisplayName, WorkflowInstanceStates.Started, this.DefinitionIdentity));
                        this.HasTrackedStarted = true;
                    }
                    else
                    {
                        this.TrackingProvider.AddRecord(new WorkflowInstanceRecord(this.Id, this.WorkflowDefinition.DisplayName, WorkflowInstanceStates.Resumed, this.DefinitionIdentity));
                    }
                }
                this.hasTrackedResumed = true;
            }
        }

        private void Run()
        {
            // validate we're in an ok state
            this.ThrowIfAborted();

            this.TrackResumed();

            // and let the scheduler go
            this.executor.MarkSchedulerRunning();
        }

        private void ScheduleCancel()
        {
            // validate we're in an ok state
            this.ThrowIfAborted();

            this.TrackResumed();

            this.executor.CancelRootActivity();
        }

        private BookmarkResumptionResult ScheduleBookmarkResumption(Bookmark bookmark, object value)
        {
            // validate we're in an ok state
            this.ValidateScheduleResumeBookmark();

            this.TrackResumed();

            return this.executor.TryResumeHostBookmark(bookmark, value);
        }

        private BookmarkResumptionResult ScheduleBookmarkResumption(Bookmark bookmark, object value, BookmarkScope scope)
        {
            // validate we're in an ok state
            this.ValidateScheduleResumeBookmark();

            this.TrackResumed();

            return this.executor.TryResumeBookmark(bookmark, value, scope);
        }

        private void ThrowIfAborted()
        {
            if (this.isAborted || (this.executor != null && this.executor.IsAbortPending))
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WorkflowInstanceAborted(this.Id)));
            }
        }

        private void ThrowIfNotIdle()
        {
            if (!this.executor.IsIdle)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.BookmarksOnlyResumableWhileIdle));
            }
        }

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.NestedTypesShouldNotBeVisible,
        //    Justification = "these are effectively protected methods, but encapsulated in a struct to avoid naming conflicts")]
        protected struct WorkflowInstanceControl : IEquatable<WorkflowInstanceControl>
        {
            private readonly ActivityExecutor executor;
            private readonly WorkflowInstance instance;

            internal WorkflowInstanceControl(WorkflowInstance instance, ActivityExecutor executor)
            {
                this.instance = instance;
                this.executor = executor;
            }

            public bool IsPersistable => this.executor.IsPersistable;

            public bool HasPendingTrackingRecords => this.instance.HasTrackingParticipant && this.instance.TrackingProvider.HasPendingRecords;

            public bool TrackingEnabled => this.instance.HasTrackingParticipant && this.instance.TrackingProvider.ShouldTrackWorkflowInstanceRecords;

            public WorkflowInstanceState State
            {
                get
                {
                    WorkflowInstanceState result;

                    if (this.instance.isAborted)
                    {
                        result = WorkflowInstanceState.Aborted;
                    }
                    else if (!this.executor.IsIdle)
                    {
                        result = WorkflowInstanceState.Runnable;
                    }
                    else
                    {
                        if (this.executor.State == ActivityInstanceState.Executing)
                        {
                            result = WorkflowInstanceState.Idle;
                        }
                        else
                        {
                            result = WorkflowInstanceState.Complete;
                        }
                    }

                    return result;
                }
            }

            public override bool Equals(object obj)
            {
                if (!(obj is WorkflowInstanceControl))
                {
                    return false;
                }

                var other = (WorkflowInstanceControl)obj;
                return other.instance == this.instance;
            }

            public override int GetHashCode() => this.instance.GetHashCode();

            public static bool operator ==(WorkflowInstanceControl left, WorkflowInstanceControl right) => left.Equals(right);

            public static bool operator !=(WorkflowInstanceControl left, WorkflowInstanceControl right) => !left.Equals(right);

            public ReadOnlyCollection<BookmarkInfo> GetBookmarks()
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartReadOnlyOperation(ref resetRequired);

                    this.instance.ValidateGetBookmarks();

                    return this.executor.GetAllBookmarks();
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            public ReadOnlyCollection<BookmarkInfo> GetBookmarks(BookmarkScope scope)
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartReadOnlyOperation(ref resetRequired);

                    this.instance.ValidateGetBookmarks();

                    return this.executor.GetBookmarks(scope);
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            public IDictionary<string, LocationInfo> GetMappedVariables()
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartReadOnlyOperation(ref resetRequired);

                    this.instance.ValidateGetMappedVariables();

                    var mappedLocations = this.instance.executor.GatherMappableVariables();
                    if (mappedLocations != null)
                    {
                        mappedLocations = new ReadOnlyDictionary<string, LocationInfo>(mappedLocations);
                    }
                    else
                    {
                        mappedLocations = WorkflowInstance.EmptyMappedVariablesDictionary;
                    }
                    return mappedLocations;
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            public void Run()
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartOperation(ref resetRequired);

                    this.instance.Run();
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }

                this.executor.Run();
            }

            public void RequestPause() =>
                // No validations for this because we do not require calls to Pause to be
                // synchronized by the caller
                this.executor.PauseScheduler();

            // Calls Pause when IsPersistable goes from false->true
            public void PauseWhenPersistable()
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartOperation(ref resetRequired);

                    this.instance.ValidatePauseWhenPersistable();

                    this.executor.PauseWhenPersistable();
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            public void ScheduleCancel()
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartOperation(ref resetRequired);

                    this.instance.ScheduleCancel();
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            public void Terminate(Exception reason)
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartOperation(ref resetRequired);

                    this.instance.Terminate(reason);
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            public BookmarkResumptionResult ScheduleBookmarkResumption(Bookmark bookmark, object value)
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartOperation(ref resetRequired);

                    return this.instance.ScheduleBookmarkResumption(bookmark, value);
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            public BookmarkResumptionResult ScheduleBookmarkResumption(Bookmark bookmark, object value, BookmarkScope scope)
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartOperation(ref resetRequired);

                    return this.instance.ScheduleBookmarkResumption(bookmark, value, scope);
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            public void Abort()
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartOperation(ref resetRequired);

                    // No validations

                    this.executor.Dispose();

                    this.instance.Abort(null);
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            public void Abort(Exception reason)
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartOperation(ref resetRequired);

                    // No validations

                    this.executor.Abort(reason);

                    this.instance.Abort(reason);
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters,
            //    Justification = "Only want to allow WorkflowInstanceRecord subclasses for WorkflowInstance-level tracking")]
            public void Track(WorkflowInstanceRecord instanceRecord)
            {
                if (instanceRecord == null)
                {
                    throw new ArgumentNullException(nameof(instanceRecord));
                }

                if (this.instance.HasTrackingParticipant)
                {
                    this.instance.TrackingProvider.AddRecord(instanceRecord);
                }
            }

            public void FlushTrackingRecords(TimeSpan timeout) => this.instance.FlushTrackingRecords(timeout);

            public IAsyncResult BeginFlushTrackingRecords(TimeSpan timeout, AsyncCallback callback, object state) =>
                this.instance.BeginFlushTrackingRecords(timeout, callback, state);

            public void EndFlushTrackingRecords(IAsyncResult result) => this.instance.EndFlushTrackingRecords(result);

            public object PrepareForSerialization()
            {
                var resetRequired = false;

                try
                {
                    this.instance.StartReadOnlyOperation(ref resetRequired);

                    this.instance.ValidatePrepareForSerialization();

                    return this.executor.PrepareForSerialization();
                }
                finally
                {
                    this.instance.FinishOperation(ref resetRequired);
                }
            }

            public ActivityInstanceState GetCompletionState() => this.executor.State;

            //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.AvoidOutParameters,
            //    Justification = "Arch approved design. Requires the out argument for extra information provided")]
            public ActivityInstanceState GetCompletionState(out Exception terminationException)
            {
                terminationException = this.executor.TerminationException;
                return this.executor.State;
            }

            //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.AvoidOutParameters,
            //    Justification = "Arch approved design. Requires the out argument for extra information provided")]
            public ActivityInstanceState GetCompletionState(out IDictionary<string, object> outputs, out Exception terminationException)
            {
                outputs = this.executor.WorkflowOutputs;
                terminationException = this.executor.TerminationException;
                return this.executor.State;
            }

            public Exception GetAbortReason() => this.instance.abortedException;

            public bool Equals(WorkflowInstanceControl other) => other.instance == this.instance;
        }
    }
}
