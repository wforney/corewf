// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    using System;
    using System.Activities.Debugger;
    using System.Activities.DynamicUpdate;
    using System.Activities.Hosting;
    using System.Activities.Internals;
    using System.Activities.Runtime.DurableInstancing;
    using System.Activities.Tracking;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics.Tracing;
    using System.Globalization;
    using System.Runtime.Serialization;
    using System.Security;
    using System.Threading;
    using System.Transactions;

    [DataContract(Name = XD.Executor.Name, Namespace = XD.Runtime.Namespace)]
    internal partial class ActivityExecutor : IEnlistmentNotification
    {
        private static ReadOnlyCollection<BookmarkInfo>? s_emptyBookmarkInfoCollection;
        private DebugController? _debugController;
        private bool _hasRaisedWorkflowStarted;

        private Guid _instanceId;
        private bool _instanceIdSet;
        private Dictionary<ActivityInstance, AsyncOperationContext>? _activeOperations;
        private WorkflowInstance? _host;

        private ActivityInstanceMap? _instanceMap;
        private MappableObjectManager? _mappableObjectManager;
        private List<ActivityInstance>? _executingSecondaryRootInstances;
        private Queue<PersistenceWaiter>? _persistenceWaiters;

        private Quack<TransactionContextWaiter>? _transactionContextWaiters;
        private RuntimeTransactionData? _runtimeTransaction;
        private bool _isDisposed;
        private bool _shouldPauseOnCanPersist;
        private Exception? _terminationPendingException;

        private int _noPersistCount;

        private SymbolResolver? _symbolResolver;

        private bool _throwDuringSerialization;

        private CodeActivityContext? _cachedResolutionContext;
        private Location? _ignorableResultLocation;

        /// <summary>
        /// work item pools (for performance)
        /// </summary>
        private Pool<EmptyWorkItem>? _emptyWorkItemPool;

        private Pool<ExecuteActivityWorkItem>? _executeActivityWorkItemPool;
        private Pool<ExecuteSynchronousExpressionWorkItem>? _executeSynchronousExpressionWorkItemPool;
        private Pool<CompletionCallbackWrapper.CompletionWorkItem>? _completionWorkItemPool;
        private Pool<ResolveNextArgumentWorkItem>? _resolveNextArgumentWorkItemPool;

        /// <summary>
        /// context pools (for performance)
        /// </summary>
        private Pool<CodeActivityContext>? _codeActivityContextPool;

        private Pool<NativeActivityContext>? _nativeActivityContextPool;
        private bool _persistExceptions;
        private bool _havePersistExceptionsValue;

        internal ActivityExecutor()
        {
        }

        public ActivityExecutor(WorkflowInstance host)
        {
            Fx.Assert(host != null, "There must be a host.");
            this._host = host ?? throw new ArgumentNullException(nameof(host));
            this.WorkflowIdentity = host.DefinitionIdentity;

            this.SerializedBookmarkManager = new BookmarkManager();
            this.SerializedScheduler = new Scheduler(new Scheduler.Callbacks(this));
        }

        public Pool<EmptyWorkItem> EmptyWorkItemPool
        {
            get
            {
                if (this._emptyWorkItemPool == null)
                {
                    this._emptyWorkItemPool = new PoolOfEmptyWorkItems();
                }

                return this._emptyWorkItemPool;
            }
        }

        private Pool<ExecuteActivityWorkItem> ExecuteActivityWorkItemPool
        {
            get
            {
                if (this._executeActivityWorkItemPool == null)
                {
                    this._executeActivityWorkItemPool = new PoolOfExecuteActivityWorkItems();
                }

                return this._executeActivityWorkItemPool;
            }
        }

        public Pool<ExecuteSynchronousExpressionWorkItem> ExecuteSynchronousExpressionWorkItemPool
        {
            get
            {
                if (this._executeSynchronousExpressionWorkItemPool == null)
                {
                    this._executeSynchronousExpressionWorkItemPool = new PoolOfExecuteSynchronousExpressionWorkItems();
                }

                return this._executeSynchronousExpressionWorkItemPool;
            }
        }

        public Pool<CompletionCallbackWrapper.CompletionWorkItem> CompletionWorkItemPool
        {
            get
            {
                if (this._completionWorkItemPool == null)
                {
                    this._completionWorkItemPool = new PoolOfCompletionWorkItems();
                }

                return this._completionWorkItemPool;
            }
        }

        public Pool<CodeActivityContext> CodeActivityContextPool
        {
            get
            {
                if (this._codeActivityContextPool == null)
                {
                    this._codeActivityContextPool = new PoolOfCodeActivityContexts();
                }

                return this._codeActivityContextPool;
            }
        }

        public Pool<NativeActivityContext> NativeActivityContextPool
        {
            get
            {
                if (this._nativeActivityContextPool == null)
                {
                    this._nativeActivityContextPool = new PoolOfNativeActivityContexts();
                }

                return this._nativeActivityContextPool;
            }
        }

        public Pool<ResolveNextArgumentWorkItem> ResolveNextArgumentWorkItemPool
        {
            get
            {
                if (this._resolveNextArgumentWorkItemPool == null)
                {
                    this._resolveNextArgumentWorkItemPool = new PoolOfResolveNextArgumentWorkItems();
                }

                return this._resolveNextArgumentWorkItemPool;
            }
        }

        public Activity? RootActivity { get; private set; }

        public bool IsInitialized => this._host != null;

        public bool HasPendingTrackingRecords => this._host.HasTrackingParticipant && this._host.TrackingProvider.HasPendingRecords;

        public bool ShouldTrack => this._host.HasTrackingParticipant && this._host.TrackingProvider.ShouldTrack;

        public bool ShouldTrackBookmarkResumptionRecords => this._host.HasTrackingParticipant && this._host.TrackingProvider.ShouldTrackBookmarkResumptionRecords;

        public bool ShouldTrackActivityScheduledRecords => this._host.HasTrackingParticipant && this._host.TrackingProvider.ShouldTrackActivityScheduledRecords;

        public bool ShouldTrackActivityStateRecords => this._host.HasTrackingParticipant && this._host.TrackingProvider.ShouldTrackActivityStateRecords;

        public bool ShouldTrackActivityStateRecordsExecutingState => this._host.HasTrackingParticipant && this._host.TrackingProvider.ShouldTrackActivityStateRecordsExecutingState;

        public bool ShouldTrackActivityStateRecordsClosedState => this._host.HasTrackingParticipant && this._host.TrackingProvider.ShouldTrackActivityStateRecordsClosedState;

        public bool ShouldTrackCancelRequestedRecords => this._host.HasTrackingParticipant && this._host.TrackingProvider.ShouldTrackCancelRequestedRecords;

        public bool ShouldTrackFaultPropagationRecords => this._host.HasTrackingParticipant && this._host.TrackingProvider.ShouldTrackFaultPropagationRecords;

        public SymbolResolver SymbolResolver
        {
            get
            {
                if (this._symbolResolver == null)
                {
                    try
                    {
                        this._symbolResolver = this._host.GetExtension<SymbolResolver>();
                    }
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                        {
                            throw;
                        }

                        throw FxTrace.Exception.AsError(new CallbackException(SR.CallbackExceptionFromHostGetExtension(this.WorkflowInstanceId), e));
                    }
                }

                return this._symbolResolver;
            }
        }

        /// <summary>
        /// This only gets accessed by root activities which are resolving arguments. Since that
        /// could at most be the real root and any secondary roots it doesn't seem necessary to cache
        /// the empty environment.
        /// </summary>
        public LocationEnvironment EmptyEnvironment => new LocationEnvironment(this, null);

        public ActivityInstanceState State
        {
            get
            {
                if ((this._executingSecondaryRootInstances != null && this._executingSecondaryRootInstances.Count > 0) ||
                    (this.SerializedRootInstance != null && !this.SerializedRootInstance.IsCompleted))
                {
                    // As long as some root is executing we need to return executing
                    return ActivityInstanceState.Executing;
                }
                else
                {
                    return this.SerializedExecutionState;
                }
            }
        }

        /// <summary>
        /// Gets or sets the workflow identity.
        /// </summary>
        /// <value>The workflow identity.</value>
        [DataMember(EmitDefaultValue = false)]
        public WorkflowIdentity? WorkflowIdentity { get; internal set; }

        [DataMember]
        public Guid WorkflowInstanceId
        {
            get
            {
                if (!this._instanceIdSet)
                {
                    this.WorkflowInstanceId = this._host.Id;
                    if (!this._instanceIdSet)
                    {
                        throw FxTrace.Exception.AsError(new InvalidOperationException(SR.EmptyIdReturnedFromHost(this._host.GetType())));
                    }
                }

                return this._instanceId;
            }
            // Internal visibility for partial trust serialization purposes only.
            internal set
            {
                this._instanceId = value;
                this._instanceIdSet = value != Guid.Empty;
            }
        }

        public Exception? TerminationException { get; private set; }

        public bool IsRunning => !this._isDisposed && this.SerializedScheduler.IsRunning;

        public bool IsPersistable => this._noPersistCount == 0;

        public bool IsAbortPending { get; private set; }

        public bool IsIdle => this._isDisposed || this.SerializedScheduler.IsIdle;

        public bool IsTerminatePending { get; private set; }

        public bool KeysAllowed => this._host.SupportsInstanceKeys;

        public IDictionary<string, object> WorkflowOutputs => this.SerializedWorkflowOutputs;

        internal BookmarkScopeManager BookmarkScopeManager
        {
            get
            {
                if (this.RawBookmarkScopeManager == null)
                {
                    this.RawBookmarkScopeManager = new BookmarkScopeManager();
                }

                return this.RawBookmarkScopeManager;
            }
        }

        internal BookmarkScopeManager? RawBookmarkScopeManager { get; private set; }

        internal BookmarkManager RawBookmarkManager => this.SerializedBookmarkManager;

        internal MappableObjectManager MappableObjectManager
        {
            get
            {
                if (this._mappableObjectManager == null)
                {
                    this._mappableObjectManager = new MappableObjectManager();
                }

                return this._mappableObjectManager;
            }
        }

        public bool RequiresTransactionContextWaiterExists => this._transactionContextWaiters != null && this._transactionContextWaiters.Count > 0 && this._transactionContextWaiters[0].IsRequires;

        public bool HasRuntimeTransaction => this._runtimeTransaction != null;

        public Transaction CurrentTransaction
        {
            get
            {
                if (this._runtimeTransaction != null)
                {
                    return this._runtimeTransaction.ClonedTransaction;
                }
                else
                {
                    return null;
                }
            }
        }

        private static ReadOnlyCollection<BookmarkInfo> EmptyBookmarkInfoCollection
        {
            get
            {
                if (s_emptyBookmarkInfoCollection == null)
                {
                    s_emptyBookmarkInfoCollection = new ReadOnlyCollection<BookmarkInfo>(new List<BookmarkInfo>(0));
                }

                return s_emptyBookmarkInfoCollection;
            }
        }

        [DataMember(Name = XD.Executor.BookmarkManager, EmitDefaultValue = false)]
        internal BookmarkManager? SerializedBookmarkManager { get; set; }

        [DataMember(Name = XD.Executor.BookmarkScopeManager, EmitDefaultValue = false)]
        internal BookmarkScopeManager? SerializedBookmarkScopeManager
        {
            get => this.RawBookmarkScopeManager;
            set => this.RawBookmarkScopeManager = value;
        }

        [DataMember(EmitDefaultValue = false, Name = "hasTrackedStarted")]
        internal bool SerializedHasTrackedStarted { get; set; }

        [DataMember(EmitDefaultValue = false, Name = "nextTrackingRecordNumber")]
        internal long SerializedNextTrackingRecordNumber { get; set; }

        [DataMember(Name = XD.Executor.RootInstance, EmitDefaultValue = false)]
        internal ActivityInstance? SerializedRootInstance { get; set; }

        [DataMember(Name = XD.Executor.SchedulerMember, EmitDefaultValue = false)]
        internal Scheduler? SerializedScheduler { get; set; }

        [DataMember(Name = XD.Executor.ShouldRaiseMainBodyComplete, EmitDefaultValue = false)]
        internal bool SerializedShouldRaiseMainBodyComplete { get; set; }

        [DataMember(Name = XD.Executor.LastInstanceId, EmitDefaultValue = false)]
        internal long SerializedLastInstanceId { get; set; }

        [DataMember(Name = XD.Executor.RootEnvironment, EmitDefaultValue = false)]
        internal LocationEnvironment? SerializedRootEnvironment { get; set; }

        [DataMember(Name = XD.Executor.WorkflowOutputs, EmitDefaultValue = false)]
        internal IDictionary<string, object>? SerializedWorkflowOutputs { get; set; }

        [DataMember(Name = XD.Executor.MainRootCompleteBookmark, EmitDefaultValue = false)]
        internal Bookmark? SerializedMainRootCompleteBookmark { get; set; }

        [DataMember(Name = XD.Executor.ExecutionState, EmitDefaultValue = false)]
        internal ActivityInstanceState SerializedExecutionState { get; set; }

        [DataMember(EmitDefaultValue = false, Name = "handles")]
        internal List<Handle> SerializedHandles
        {
            get { return this.Handles; }
            set { this.Handles = value; }
        }

        internal bool PersistExceptions
        {
            get
            {
                if (!this._havePersistExceptionsValue)
                {
                    // If we have an ExceptionPersistenceExtension, set our cached
                    // "persistExceptions" value to its PersistExceptions property. If we don't have
                    // the extension, set the cached value to true.
                    var extension = this._host.GetExtension<ExceptionPersistenceExtension>();
                    if (extension != null)
                    {
                        this._persistExceptions = extension.PersistExceptions;
                    }
                    else
                    {
                        this._persistExceptions = true;
                    }

                    this._havePersistExceptionsValue = true;
                }
                return this._persistExceptions;
            }
        }

        [DataMember(Name = XD.Executor.CompletionException, EmitDefaultValue = false)]
        internal Exception SerializedCompletionException
        {
            get
            {
                if (this.PersistExceptions)
                {
                    return this.TerminationException;
                }
                else
                {
                    return null;
                }
            }
            set
            {
                this.TerminationException = value;
            }
        }

        [DataMember(Name = XD.Executor.TransactionContextWaiters, EmitDefaultValue = false)]
        //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode, Justification = "Used by serialization")]
        internal TransactionContextWaiter[] SerializedTransactionContextWaiters
        {
            get
            {
                if (this._transactionContextWaiters != null && this._transactionContextWaiters.Count > 0)
                {
                    return this._transactionContextWaiters.ToArray();
                }
                else
                {
                    return null;
                }
            }
            set
            {
                Fx.Assert(value != null, "We don't serialize out null.");
                this._transactionContextWaiters = new Quack<TransactionContextWaiter>(value);
            }
        }

        [DataMember(Name = XD.Executor.PersistenceWaiters, EmitDefaultValue = false)]
        //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode, Justification = "Used by serialization")]
        internal Queue<PersistenceWaiter> SerializedPersistenceWaiters
        {
            get
            {
                if (this._persistenceWaiters == null || this._persistenceWaiters.Count == 0)
                {
                    return null;
                }
                else
                {
                    return this._persistenceWaiters;
                }
            }
            set
            {
                Fx.Assert(value != null, "We don't serialize out null.");
                this._persistenceWaiters = value;
            }
        }

        [DataMember(Name = XD.Executor.SecondaryRootInstances, EmitDefaultValue = false)]
        //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode, Justification = "Used by serialization")]
        internal List<ActivityInstance> SerializedExecutingSecondaryRootInstances
        {
            get
            {
                if (this._executingSecondaryRootInstances != null && this._executingSecondaryRootInstances.Count > 0)
                {
                    return this._executingSecondaryRootInstances;
                }
                else
                {
                    return null;
                }
            }
            set
            {
                Fx.Assert(value != null, "We don't serialize out null.");
                this._executingSecondaryRootInstances = value;
            }
        }

        [DataMember(Name = XD.Executor.MappableObjectManager, EmitDefaultValue = false)]
        //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode, Justification = "Used by serialization")]
        internal MappableObjectManager? SerializedMappableObjectManager
        {
            get
            {
                if (this._mappableObjectManager == null || this._mappableObjectManager.Count == 0)
                {
                    return null;
                }

                return this._mappableObjectManager;
            }

            set
            {
                Fx.Assert(value != null, "value from serialization should never be null");
                this._mappableObjectManager = value;
            }
        }

        /// <summary>
        /// map from activity names to (active) associated activity instances
        /// </summary>
        [DataMember(Name = XD.Executor.ActivityInstanceMap, EmitDefaultValue = false)]
        //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode, Justification = "called from serialization")]
        internal ActivityInstanceMap SerializedProgramMapping
        {
            get
            {
                this.ThrowIfNonSerializable();

                if (this._instanceMap == null && !this._isDisposed)
                {
                    this._instanceMap = new ActivityInstanceMap();

                    this.SerializedRootInstance.FillInstanceMap(this._instanceMap);
                    this.SerializedScheduler.FillInstanceMap(this._instanceMap);

                    if (this._executingSecondaryRootInstances != null && this._executingSecondaryRootInstances.Count > 0)
                    {
                        foreach (var secondaryRoot in this._executingSecondaryRootInstances)
                        {
                            secondaryRoot.FillInstanceMap(this._instanceMap);

                            var environment = secondaryRoot.Environment;

                            if (secondaryRoot.IsEnvironmentOwner)
                            {
                                environment = environment.Parent;
                            }

                            while (environment != null)
                            {
                                if (environment.HasOwnerCompleted)
                                {
                                    this._instanceMap.AddEntry(environment, true);
                                }

                                environment = environment.Parent;
                            }
                        }
                    }
                }

                return this._instanceMap;
            }

            set
            {
                Fx.Assert(value != null, "value from serialization should never be null");
                this._instanceMap = value;
            }
        }

        /// <summary>
        /// may be null
        /// </summary>
        internal ExecutionPropertyManager? RootPropertyManager { get; private set; }

        [DataMember(Name = XD.ActivityInstance.PropertyManager, EmitDefaultValue = false)]
        //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode, Justification = "Called from Serialization")]
        internal ExecutionPropertyManager SerializedPropertyManager
        {
            get
            {
                return this.RootPropertyManager;
            }
            set
            {
                Fx.Assert(value != null, "We don't emit the default value so this should never be null.");
                this.RootPropertyManager = value;
                this.RootPropertyManager.OnDeserialized(null, null, null, this);
            }
        }

        public void ThrowIfNonSerializable()
        {
            if (this._throwDuringSerialization)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.StateCannotBeSerialized(this.WorkflowInstanceId)));
            }
        }

        public void MakeNonSerializable() => this._throwDuringSerialization = true;

        public IList<ActivityBlockingUpdate> GetActivitiesBlockingUpdate(DynamicUpdateMap updateMap)
        {
            Fx.Assert(updateMap != null, "UpdateMap must not be null.");
            Collection<ActivityBlockingUpdate>? result = null;
            this._instanceMap.GetActivitiesBlockingUpdate(updateMap, this._executingSecondaryRootInstances, ref result);
            return result;
        }

        public void UpdateInstancePhase1(DynamicUpdateMap updateMap, Activity targetDefinition, ref Collection<ActivityBlockingUpdate>? updateErrors)
        {
            if (updateMap == null)
            {
                throw new ArgumentNullException(nameof(updateMap));
            }

            Fx.Assert(updateMap != null, "UpdateMap must not be null.");

            this._instanceMap.UpdateRawInstance(updateMap, targetDefinition, this._executingSecondaryRootInstances, ref updateErrors);
        }

        public void UpdateInstancePhase2(DynamicUpdateMap updateMap, ref Collection<ActivityBlockingUpdate>? updateErrors) =>
            this._instanceMap.UpdateInstanceByActivityParticipation(this, updateMap, ref updateErrors);

        /// <summary>
        /// Gets the handles.
        /// </summary>
        /// <value>The handles.</value>
        internal List<Handle>? Handles { get; private set; }

        /// <summary>
        /// evaluate an argument/variable expression using fast-path optimizations
        /// </summary>
        /// <param name="parentInstance"></param>
        /// <param name="expressionActivity"></param>
        /// <param name="instanceId"></param>
        /// <param name="resultLocation"></param>
        public void ExecuteInResolutionContextUntyped(ActivityInstance parentInstance, ActivityWithResult expressionActivity, long instanceId, Location resultLocation)
        {
            if (this._cachedResolutionContext == null)
            {
                this._cachedResolutionContext = new CodeActivityContext(parentInstance, this);
            }

            this._cachedResolutionContext.Reinitialize(parentInstance, this, expressionActivity, instanceId);
            try
            {
                this._ignorableResultLocation = resultLocation;
                resultLocation.Value = expressionActivity.InternalExecuteInResolutionContextUntyped(this._cachedResolutionContext);
            }
            finally
            {
                if (!expressionActivity.UseOldFastPath)
                {
                    // The old fast path allows WorkflowDataContexts to escape up one level, because
                    // the resolution context uses the parent's ActivityInstance. We support that
                    // for back-compat, but don't allow it on new fast-path activities.
                    this._cachedResolutionContext.DisposeDataContext();
                }

                this._cachedResolutionContext.Dispose();
                this._ignorableResultLocation = null;
            }
        }

        /// <summary>
        /// evaluate an argument/variable expression using fast-path optimizations
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parentInstance"></param>
        /// <param name="expressionActivity"></param>
        /// <returns></returns>
        public T ExecuteInResolutionContext<T>(ActivityInstance parentInstance, Activity<T> expressionActivity)
        {
            Fx.Assert(expressionActivity.UseOldFastPath, "New fast path should be scheduled via ExecuteSynchronousExpressionWorkItem, which calls the Untyped overload");

            if (this._cachedResolutionContext == null)
            {
                this._cachedResolutionContext = new CodeActivityContext(parentInstance, this);
            }

            this._cachedResolutionContext.Reinitialize(parentInstance, this, expressionActivity, parentInstance.InternalId);
            T result;
            try
            {
                result = expressionActivity.InternalExecuteInResolutionContext(this._cachedResolutionContext);
            }
            finally
            {
                this._cachedResolutionContext.Dispose();
            }
            return result;
        }

        internal void ExecuteSynchronousWorkItem(WorkItem workItem)
        {
            workItem.Release(this);
            try
            {
                var result = workItem.Execute(this, this.SerializedBookmarkManager);
                Fx.AssertAndThrow(result, "Synchronous work item should not yield the scheduler");
            }
            finally
            {
                workItem.Dispose(this);
            }
        }

        internal void ExitNoPersistForExceptionPropagation()
        {
            if (!this.PersistExceptions)
            {
                this.ExitNoPersist();
            }
        }

        /// <summary>
        /// This is called by RuntimeArgument.GetLocation (via
        /// ActivityContext.GetIgnorableResultLocation) when the user tries to access the Result
        /// argument on an activity being run with SkipArgumentResolution.
        /// </summary>
        /// <param name="resultArgument"></param>
        /// <returns></returns>
        internal Location? GetIgnorableResultLocation(RuntimeArgument resultArgument)
        {
            Fx.Assert(resultArgument.Owner == this._cachedResolutionContext.Activity, "GetIgnorableResultLocation should only be called for activity in resolution context");
            Fx.Assert(this._ignorableResultLocation != null, "ResultLocation should have been passed in to ExecuteInResolutionContext");

            return this._ignorableResultLocation;
        }

        /// <summary>
        /// Whether it is being debugged.
        /// </summary>
        /// <returns></returns>
        private bool IsDebugged()
        {
            if (this._debugController == null)
            {
#if DEBUG
                if (Fx.StealthDebugger)
                {
                    return false;
                }
#endif
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    this._debugController = new DebugController(this._host);
                }
            }
            return this._debugController != null;
        }

        public void DebugActivityCompleted(ActivityInstance instance)
        {
            if (this._debugController != null)   // Don't use IsDebugged() for perf reason.
            {
                this._debugController.ActivityCompleted(instance);
            }
        }

        public void AddTrackingRecord(TrackingRecord record)
        {
            Fx.Assert(this._host?.TrackingProvider != null, Properties.Resources.WeShouldOnlyAddRecordsIfWeHaveATrackingProvider);
            if (this._host?.TrackingProvider == null)
            {
                throw new NullReferenceException(Properties.Resources.WeShouldOnlyAddRecordsIfWeHaveATrackingProvider);
            }

            this._host?.TrackingProvider?.AddRecord(record);
        }

        public bool ShouldTrackActivity(string name)
        {
            Fx.Assert(this._host?.TrackingProvider != null, Properties.Resources.WeShouldOnlyAddRecordsIfWeHaveATrackingProvider);
            if (this._host?.TrackingProvider == null)
            {
                throw new NullReferenceException(Properties.Resources.WeShouldOnlyAddRecordsIfWeHaveATrackingProvider);
            }

            return this._host.TrackingProvider.ShouldTrackActivity(name);
        }

        public IAsyncResult BeginTrackPendingRecords(AsyncCallback callback, object state)
        {
            Fx.Assert(this._host?.TrackingProvider != null, Properties.Resources.WeShouldOnlyTryToTrackIfWeHaveATrackingProvider);
            if (this._host?.TrackingProvider == null)
            {
                throw new NullReferenceException(Properties.Resources.WeShouldOnlyTryToTrackIfWeHaveATrackingProvider);
            }

            return this._host.BeginFlushTrackingRecordsInternal(callback, state);
        }

        public void EndTrackPendingRecords(IAsyncResult result)
        {
            Fx.Assert(this._host?.TrackingProvider != null, Properties.Resources.WeShouldOnlyTryToTrackIfWeHaveATrackingProvider);
            if (this._host?.TrackingProvider == null)
            {
                throw new NullReferenceException(Properties.Resources.WeShouldOnlyTryToTrackIfWeHaveATrackingProvider);
            }

            this._host.EndFlushTrackingRecordsInternal(result);
        }

        internal IDictionary<string, LocationInfo>? GatherMappableVariables() => this._mappableObjectManager == null ? null : this.MappableObjectManager.GatherMappableVariables();

        internal void OnSchedulerThreadAcquired()
        {
            if (this.IsDebugged() && !this._hasRaisedWorkflowStarted)
            {
                this._hasRaisedWorkflowStarted = true;
                this._debugController?.WorkflowStarted();
            }
        }

        public void Dispose() => this.Dispose(true);

        private void Dispose(bool aborting)
        {
            if (!this._isDisposed)
            {
                if (this._debugController != null)   // Don't use IsDebugged() because it may create debugController unnecessarily.
                {
                    this._debugController.WorkflowCompleted();
                    this._debugController = null;
                }

                if (this._activeOperations != null && this._activeOperations.Count > 0)
                {
                    Fx.Assert(aborting, "shouldn't get here in the graceful close case");
                    this.Abort(new OperationCanceledException());
                }
                else
                {
                    this.SerializedScheduler?.ClearAllWorkItems(this);

                    if (!aborting)
                    {
                        this.SerializedScheduler = null;
                        this.SerializedBookmarkManager = null;
                        this.SerializedLastInstanceId = 0;
                        this.SerializedRootInstance = null;
                    }

                    this._isDisposed = true;
                }
            }
        }

        /// <summary>
        /// Called from an arbitrary thread
        /// </summary>
        public void PauseWhenPersistable() => this._shouldPauseOnCanPersist = true;

        public void EnterNoPersist()
        {
            this._noPersistCount++;

            if (TD.EnterNoPersistBlockIsEnabled())
            {
                TD.EnterNoPersistBlock();
            }
        }

        public void ExitNoPersist()
        {
            this._noPersistCount--;

            if (TD.ExitNoPersistBlockIsEnabled())
            {
                TD.ExitNoPersistBlock();
            }

            if (this._shouldPauseOnCanPersist && this.IsPersistable)
            {
                // shouldPauseOnCanPersist is reset at the next pause notification
                this.SerializedScheduler?.Pause();
            }
        }

        void IEnlistmentNotification.Commit(Enlistment enlistment)
        {
            // Because of ordering we might get this notification after we've already determined the outcome

            // Get a local copy of _runtimeTransaction because it is possible for
            // _runtimeTransaction to be nulled out between the time we check for null and the time
            // we try to lock it.
            var localRuntimeTransaction = this._runtimeTransaction;

            if (localRuntimeTransaction != null)
            {
                AsyncWaitHandle? completionEvent = null;

                lock (localRuntimeTransaction)
                {
                    completionEvent = localRuntimeTransaction.CompletionEvent;

                    localRuntimeTransaction.TransactionStatus = TransactionStatus.Committed;
                }

                enlistment.Done();

                if (completionEvent != null)
                {
                    completionEvent.Set();
                }
            }
            else
            {
                enlistment.Done();
            }
        }

        void IEnlistmentNotification.InDoubt(Enlistment enlistment) => ((IEnlistmentNotification)this).Rollback(enlistment);

        /// <summary>
        ///Note - There is a scenario in the TransactedReceiveScope while dealing with server side WCF dispatcher created transactions,
        ///the activity instance will end up calling BeginCommit before finishing up its execution. By this we allow the executing TransactedReceiveScope activity to
        ///complete and the executor is "free" to respond to this Prepare notification as part of the commit processing of that server side transaction
        /// </summary>
        /// <param name="preparingEnlistment"></param>
        void IEnlistmentNotification.Prepare(PreparingEnlistment preparingEnlistment)
        {
            // Because of ordering we might get this notification after we've already determined the outcome

            // Get a local copy of _runtimeTransaction because it is possible for
            // _runtimeTransaction to be nulled out between the time we check for null and the time
            // we try to lock it.
            var localRuntimeTransaction = this._runtimeTransaction;

            if (localRuntimeTransaction != null)
            {
                var callPrepared = false;

                lock (localRuntimeTransaction)
                {
                    if (localRuntimeTransaction.HasPrepared)
                    {
                        callPrepared = true;
                    }
                    else
                    {
                        localRuntimeTransaction.PendingPreparingEnlistment = preparingEnlistment;
                    }
                }

                if (callPrepared)
                {
                    preparingEnlistment.Prepared();
                }
            }
            else
            {
                preparingEnlistment.Prepared();
            }
        }

        void IEnlistmentNotification.Rollback(Enlistment enlistment)
        {
            // Because of ordering we might get this notification after we've already determined the outcome

            // Get a local copy of _runtimeTransaction because it is possible for
            // _runtimeTransaction to be nulled out between the time we check for null and the time
            // we try to lock it.
            var localRuntimeTransaction = this._runtimeTransaction;

            if (localRuntimeTransaction != null)
            {
                AsyncWaitHandle completionEvent = null;

                lock (localRuntimeTransaction)
                {
                    completionEvent = localRuntimeTransaction.CompletionEvent;

                    localRuntimeTransaction.TransactionStatus = TransactionStatus.Aborted;
                }

                enlistment.Done();

                if (completionEvent != null)
                {
                    completionEvent.Set();
                }
            }
            else
            {
                enlistment.Done();
            }
        }

        public void RequestTransactionContext(ActivityInstance instance, bool isRequires, RuntimeTransactionHandle handle, Action<NativeActivityTransactionContext, object> callback, object state)
        {
            if (isRequires)
            {
                this.EnterNoPersist();
            }

            if (this._transactionContextWaiters == null)
            {
                this._transactionContextWaiters = new Quack<TransactionContextWaiter>();
            }

            var waiter = new TransactionContextWaiter(instance, isRequires, handle, new TransactionContextWaiterCallbackWrapper(callback, instance), state);

            if (isRequires)
            {
                Fx.Assert(this._transactionContextWaiters.Count == 0 || !this._transactionContextWaiters[0].IsRequires, "Either we don't have any waiters or the first one better not be IsRequires == true");

                this._transactionContextWaiters.PushFront(waiter);
            }
            else
            {
                this._transactionContextWaiters.Enqueue(waiter);
            }

            instance.IncrementBusyCount();
            instance.WaitingForTransactionContext = true;
        }

        public void SetTransaction(RuntimeTransactionHandle handle, Transaction transaction, ActivityInstance isolationScope, ActivityInstance transactionOwner)
        {
            this._runtimeTransaction = new RuntimeTransactionData(handle, transaction, isolationScope);
            this.EnterNoPersist();

            // no more work to do for a host-declared transaction
            if (transactionOwner == null)
            {
                return;
            }

            Exception abortException = null;

            try
            {
                transaction.EnlistVolatile(this, EnlistmentOptions.EnlistDuringPrepareRequired);
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
                this.AbortWorkflowInstance(abortException);
            }
            else
            {
                if (TD.RuntimeTransactionSetIsEnabled())
                {
                    Fx.Assert(transactionOwner != null, "isolationScope and transactionOwner are either both null or both non-null");
                    TD.RuntimeTransactionSet(transactionOwner.Activity.GetType().ToString(), transactionOwner.Activity.DisplayName, transactionOwner.Id, isolationScope.Activity.GetType().ToString(), isolationScope.Activity.DisplayName, isolationScope.Id);
                }
            }
        }

        public void CompleteTransaction(RuntimeTransactionHandle handle, BookmarkCallback callback, ActivityInstance callbackOwner)
        {
            if (callback != null)
            {
                var bookmark = this.SerializedBookmarkManager.CreateBookmark(callback, callbackOwner, BookmarkOptions.None);

                ActivityInstance isolationScope = null;

                if (this._runtimeTransaction != null)
                {
                    isolationScope = this._runtimeTransaction.IsolationScope;
                }

                this.SerializedBookmarkManager.TryGenerateWorkItem(this, false, ref bookmark, null, isolationScope, out var workItem);
                this.SerializedScheduler.EnqueueWork(workItem);
            }

            if (this._runtimeTransaction != null && this._runtimeTransaction.TransactionHandle == handle)
            {
                this._runtimeTransaction.ShouldScheduleCompletion = true;

                if (TD.RuntimeTransactionCompletionRequestedIsEnabled())
                {
                    TD.RuntimeTransactionCompletionRequested(callbackOwner.Activity.GetType().ToString(), callbackOwner.Activity.DisplayName, callbackOwner.Id);
                }
            }
        }

        private void SchedulePendingCancelation()
        {
            if (this._runtimeTransaction.IsRootCancelPending)
            {
                if (!this.SerializedRootInstance.IsCancellationRequested && !this.SerializedRootInstance.IsCompleted)
                {
                    this.SerializedRootInstance.IsCancellationRequested = true;
                    this.SerializedScheduler.PushWork(new CancelActivityWorkItem(this.SerializedRootInstance));
                }

                this._runtimeTransaction.IsRootCancelPending = false;
            }
        }

        public EmptyWorkItem CreateEmptyWorkItem(ActivityInstance instance)
        {
            var workItem = this.EmptyWorkItemPool.Acquire();
            workItem.Initialize(instance);

            return workItem;
        }

        public bool IsCompletingTransaction(ActivityInstance instance)
        {
            if (this._runtimeTransaction != null && this._runtimeTransaction.IsolationScope == instance)
            {
                // We add an empty work item to keep the instance alive
                this.SerializedScheduler.PushWork(this.CreateEmptyWorkItem(instance));

                // This will schedule the appopriate work item at the end of this work item
                this._runtimeTransaction.ShouldScheduleCompletion = true;

                if (TD.RuntimeTransactionCompletionRequestedIsEnabled())
                {
                    TD.RuntimeTransactionCompletionRequested(instance.Activity.GetType().ToString(), instance.Activity.DisplayName, instance.Id);
                }

                return true;
            }

            return false;
        }

        public void TerminateSpecialExecutionBlocks(ActivityInstance terminatedInstance, Exception terminationReason)
        {
            if (this._runtimeTransaction != null && this._runtimeTransaction.IsolationScope == terminatedInstance)
            {
                Exception abortException = null;

                try
                {
                    this._runtimeTransaction.Rollback(terminationReason);
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
                    // It is okay for us to call AbortWorkflowInstance even if we are already
                    // aborting the instance since it is an async call (IE - we asking the host to
                    // re-enter the instance to abandon it.
                    this.AbortWorkflowInstance(abortException);
                }

                this.SchedulePendingCancelation();

                this.ExitNoPersist();

                if (this._runtimeTransaction.TransactionHandle.AbortInstanceOnTransactionFailure)
                {
                    this.AbortWorkflowInstance(terminationReason);
                }

                this._runtimeTransaction = null;
            }
        }

        // Returns true if we actually performed the abort and false if we had already been disposed
        private bool Abort(Exception terminationException, bool isTerminate)
        {
            if (!this._isDisposed)
            {
                if (!this.SerializedRootInstance.IsCompleted)
                {
                    this.SerializedRootInstance.Abort(this, this.SerializedBookmarkManager, terminationException, isTerminate);

                    // the Abort walk won't catch host-registered properties
                    if (this.RootPropertyManager != null)
                    {
                        if (isTerminate)
                        {
                            var context = new HandleInitializationContext(this, null);
                            foreach (var executionProperty in this.RootPropertyManager.Properties.Values)
                            {
                                if (executionProperty.Property is Handle handle)
                                {
                                    handle.Uninitialize(context);
                                }
                            }
                            context.Dispose();
                        }

                        this.RootPropertyManager.UnregisterProperties(null, null, true);
                    }
                }

                if (this._executingSecondaryRootInstances != null)
                {
                    // We have to walk this list backwards because the abort path removes from this collection.
                    for (var i = this._executingSecondaryRootInstances.Count - 1; i >= 0; i--)
                    {
                        var secondaryRootInstance = this._executingSecondaryRootInstances[i];

                        Fx.Assert(!secondaryRootInstance.IsCompleted, "We should not have any complete instances in our list.");

                        secondaryRootInstance.Abort(this, this.SerializedBookmarkManager, terminationException, isTerminate);

                        Fx.Assert(this._executingSecondaryRootInstances.Count == i, "We are always working from the back and we should have removed the item we just aborted.");
                    }
                }

                // This must happen after we abort each activity. This allows us to utilize code
                // paths which schedule work items.
                this.SerializedScheduler.ClearAllWorkItems(this);

                if (isTerminate)
                {
                    // Regardless of the previous state, a termination implies setting the
                    // completion exception and completing in the Faulted state.
                    this.TerminationException = terminationException;
                    this.SerializedExecutionState = ActivityInstanceState.Faulted;
                }

                this.Dispose();

                return true;
            }

            return false;
        }

        // Returns true if tracing was transfered
        private bool TryTraceResume(out Guid oldActivityId)
        {
            if (TD.IsEnd2EndActivityTracingEnabled() && TD.ShouldTraceToTraceSource(EventLevel.Informational))
            {
                oldActivityId = TD.CurrentActivityId;
                TD.TraceTransfer(this.WorkflowInstanceId);

                if (TD.WorkflowActivityResumeIsEnabled())
                {
                    TD.WorkflowActivityResume(this.WorkflowInstanceId);
                }

                return true;
            }
            else
            {
                oldActivityId = Guid.Empty;
                return false;
            }
        }

        // Returns true if tracing was transfered
        private bool TryTraceStart(out Guid oldActivityId)
        {
            if (TD.IsEnd2EndActivityTracingEnabled() && TD.ShouldTraceToTraceSource(EventLevel.Informational))
            {
                oldActivityId = TD.CurrentActivityId;
                TD.TraceTransfer(this.WorkflowInstanceId);

                if (TD.WorkflowActivityStartIsEnabled())
                {
                    TD.WorkflowActivityStart(this.WorkflowInstanceId);
                }

                return true;
            }
            else
            {
                oldActivityId = Guid.Empty;
                return false;
            }
        }

        private void TraceSuspend(bool hasBeenResumed, Guid oldActivityId)
        {
            if (hasBeenResumed)
            {
                if (TD.WorkflowActivitySuspendIsEnabled())
                {
                    TD.WorkflowActivitySuspend(this.WorkflowInstanceId);
                }

                TD.CurrentActivityId = oldActivityId;
            }
        }

        public bool Abort(Exception reason)
        {
            var hasTracedResume = this.TryTraceResume(out var oldActivityId);

            var abortResult = this.Abort(reason, false);

            this.TraceSuspend(hasTracedResume, oldActivityId);

            return abortResult;
        }

        // It must be okay for the runtime to be processing other work on a different thread when
        // this is called. See the comments in the method for justifications.
        public void AbortWorkflowInstance(Exception reason)
        {
            // 1) This flag is only ever set to true
            this.IsAbortPending = true;

            // 2) This causes a couple of fields to be set
            this._host.Abort(reason);
            try
            {
                // 3) The host expects this to come from an unknown thread
                this._host.OnRequestAbort(reason);
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }
                throw FxTrace.Exception.AsError(new CallbackException(SR.CallbackExceptionFromHostAbort(this.WorkflowInstanceId), e));
            }
        }

        public void ScheduleTerminate(Exception reason)
        {
            this.IsTerminatePending = true;
            this._terminationPendingException = reason;
        }

        public void Terminate(Exception reason)
        {
            Fx.Assert(!this._isDisposed, "We should not have been able to get here if we are disposed and Abort makes choices based on isDisposed");

            var hasTracedResume = this.TryTraceResume(out var oldActivityId);

            this.Abort(reason, true);

            this.TraceSuspend(hasTracedResume, oldActivityId);
        }

        public void CancelRootActivity()
        {
            if (this.SerializedRootInstance.State == ActivityInstanceState.Executing)
            {
                if (!this.SerializedRootInstance.IsCancellationRequested)
                {
                    var hasTracedResume = this.TryTraceResume(out var oldActivityId);

                    var trackCancelRequested = true;

                    if (this._runtimeTransaction != null && this._runtimeTransaction.IsolationScope != null)
                    {
                        if (this._runtimeTransaction.IsRootCancelPending)
                        {
                            trackCancelRequested = false;
                        }

                        this._runtimeTransaction.IsRootCancelPending = true;
                    }
                    else
                    {
                        this.SerializedRootInstance.IsCancellationRequested = true;

                        if (this.SerializedRootInstance.HasNotExecuted)
                        {
                            this.SerializedScheduler.PushWork(this.CreateEmptyWorkItem(this.SerializedRootInstance));
                        }
                        else
                        {
                            this.SerializedScheduler.PushWork(new CancelActivityWorkItem(this.SerializedRootInstance));
                        }
                    }

                    if (this.ShouldTrackCancelRequestedRecords && trackCancelRequested)
                    {
                        this.AddTrackingRecord(new CancelRequestedRecord(this.WorkflowInstanceId, null, this.SerializedRootInstance));
                    }

                    this.TraceSuspend(hasTracedResume, oldActivityId);
                }
            }
            else if (this.SerializedRootInstance.State != ActivityInstanceState.Closed)
            {
                // We've been asked to cancel the instance and the root completed in a canceled or
                // faulted state. By our rules this means that the instance has been canceled. A
                // real world example if the case of UnhandledExceptionAction.Cancel on a workflow
                // whose root activity threw an exception. The expected completion state is Canceled
                // and NOT Faulted.
                this.SerializedExecutionState = ActivityInstanceState.Canceled;
                this.TerminationException = null;
            }
        }

        public void CancelActivity(ActivityInstance activityInstance)
        {
            Fx.Assert(activityInstance != null, "The instance must not be null.");

            // Cancel is a no-op if the activity is complete or cancel has already been requested
            if (activityInstance.State != ActivityInstanceState.Executing || activityInstance.IsCancellationRequested)
            {
                return;
            }

            // Set that we have requested cancel. This is our only guard against scheduling
            // ActivityInstance.Cancel multiple times.
            activityInstance.IsCancellationRequested = true;

            if (activityInstance.HasNotExecuted)
            {
                this.SerializedScheduler.PushWork(this.CreateEmptyWorkItem(activityInstance));
            }
            else
            {
                this.SerializedScheduler.PushWork(new CancelActivityWorkItem(activityInstance));
            }

            if (this.ShouldTrackCancelRequestedRecords)
            {
                this.AddTrackingRecord(new CancelRequestedRecord(this.WorkflowInstanceId, activityInstance.Parent, activityInstance));
            }
        }

        private void PropagateException(WorkItem workItem)
        {
            var exceptionSource = workItem.ActivityInstance;
            var exception = workItem.ExceptionToPropagate;

            var exceptionPropagator = exceptionSource;
            FaultBookmark targetBookmark = null;

            // If we are not supposed to persist exceptions, call EnterNoPersist so that we don't
            // persist while we are propagating the exception. We call ExitNoPersist when we abort
            // an activity or when we call a fault callback. But we may end up re-propagating and
            // thus calling EnterNoPersist again. We also do an exit if the workflow is aborted or
            // the exception ends up being unhandled.
            if (!this.PersistExceptions)
            {
                this.EnterNoPersist();
            }
            while (exceptionPropagator != null && targetBookmark == null)
            {
                if (!exceptionPropagator.IsCompleted)
                {
                    if (this._runtimeTransaction != null && this._runtimeTransaction.IsolationScope == exceptionPropagator)
                    {
                        // We are propagating the exception across the isolation scope
                        this.SerializedScheduler.PushWork(new AbortActivityWorkItem(this, exceptionPropagator, exception, this.CreateActivityInstanceReference(workItem.OriginalExceptionSource, exceptionPropagator)));

                        // Because we are aborting the transaction we reset the
                        // ShouldScheduleCompletion flag
                        this._runtimeTransaction.ShouldScheduleCompletion = false;
                        workItem.ExceptionPropagated();
                        return;
                    }
                }

                if (exceptionPropagator.IsCancellationRequested)
                {
                    // Regardless of whether it is already completed or not we need to honor the
                    // workflow abort

                    this.AbortWorkflowInstance(new InvalidOperationException(SR.CannotPropagateExceptionWhileCanceling(exceptionSource.Activity.DisplayName, exceptionSource.Id), exception));
                    workItem.ExceptionPropagated();
                    this.ExitNoPersistForExceptionPropagation();
                    return;
                }

                if (exceptionPropagator.FaultBookmark != null)
                {
                    // This will cause us to break out of the loop
                    targetBookmark = exceptionPropagator.FaultBookmark;
                }
                else
                {
                    exceptionPropagator = exceptionPropagator.Parent;
                }
            }

            if (targetBookmark != null)
            {
                if (this.ShouldTrackFaultPropagationRecords)
                {
                    this.AddTrackingRecord(new FaultPropagationRecord(this.WorkflowInstanceId,
                                                                workItem.OriginalExceptionSource,
                                                                exceptionPropagator.Parent,
                                                                exceptionSource == workItem.OriginalExceptionSource,
                                                                exception));
                }

                this.SerializedScheduler.PushWork(targetBookmark.GenerateWorkItem(exception, exceptionPropagator, this.CreateActivityInstanceReference(workItem.OriginalExceptionSource, exceptionPropagator.Parent)));
                workItem.ExceptionPropagated();
            }
            else
            {
                if (this.ShouldTrackFaultPropagationRecords)
                {
                    this.AddTrackingRecord(new FaultPropagationRecord(this.WorkflowInstanceId,
                                                                workItem.OriginalExceptionSource,
                                                                null,
                                                                exceptionSource == workItem.OriginalExceptionSource,
                                                                exception));
                }
            }
        }

        internal ActivityInstanceReference CreateActivityInstanceReference(ActivityInstance toReference, ActivityInstance referenceOwner)
        {
            var reference = new ActivityInstanceReference(toReference);

            if (this._instanceMap != null)
            {
                this._instanceMap.AddEntry(reference);
            }

            referenceOwner.AddActivityReference(reference);

            return reference;
        }

        internal void RethrowException(ActivityInstance fromInstance, FaultContext context) => this.SerializedScheduler.PushWork(new RethrowExceptionWorkItem(fromInstance, context.Exception, context.Source));

        internal void OnDeserialized(Activity workflow, WorkflowInstance workflowInstance)
        {
            Fx.Assert(workflow != null, "The program must be non-null");
            Fx.Assert(workflowInstance != null, "The host must be non-null");

            if (!Equals(workflowInstance.DefinitionIdentity, this.WorkflowIdentity))
            {
                throw FxTrace.Exception.AsError(new VersionMismatchException(workflowInstance.DefinitionIdentity, this.WorkflowIdentity));
            }

            this.RootActivity = workflow;
            this._host = workflowInstance;

            if (!this._instanceIdSet)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.EmptyGuidOnDeserializedInstance));
            }
            if (this._host.Id != this._instanceId)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.HostIdDoesNotMatchInstance(this._host.Id, this._instanceId)));
            }

            if (this._host.HasTrackingParticipant)
            {
                this._host.TrackingProvider.OnDeserialized(this.SerializedNextTrackingRecordNumber);
                this._host.OnDeserialized(this.SerializedHasTrackedStarted);
            }

            // hookup our callback to the scheduler
            if (this.SerializedScheduler != null)
            {
                this.SerializedScheduler.OnDeserialized(new Scheduler.Callbacks(this));
            }

            if (this.SerializedRootInstance != null)
            {
                Fx.Assert(this._instanceMap != null, "We always have an InstanceMap.");
                this._instanceMap.LoadActivityTree(workflow, this.SerializedRootInstance, this._executingSecondaryRootInstances, this);

                // We need to make sure that any "dangling" secondary root environments get
                // OnDeserialized called.
                if (this._executingSecondaryRootInstances != null)
                {
                    Fx.Assert(this._executingSecondaryRootInstances.Count > 0, "We don't serialize out an empty list.");

                    for (var i = 0; i < this._executingSecondaryRootInstances.Count; i++)
                    {
                        var secondaryRoot = this._executingSecondaryRootInstances[i];
                        var environment = secondaryRoot.Environment.Parent;

                        if (environment != null)
                        {
                            environment.OnDeserialized(this, secondaryRoot);
                        }
                    }
                }
            }
            else
            {
                this._isDisposed = true;
            }
        }

        public T GetExtension<T>()
            where T : class
        {
            T extension = null;
            try
            {
                extension = this._host.GetExtension<T>();
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }
                throw FxTrace.Exception.AsError(new CallbackException(SR.CallbackExceptionFromHostGetExtension(this.WorkflowInstanceId), e));
            }

            return extension;
        }

        internal Scheduler.RequestedAction TryExecuteNonEmptyWorkItem(WorkItem workItem)
        {
            Exception setupOrCleanupException = null;
            var propertyManagerOwner = workItem.PropertyManagerOwner;
            try
            {
                if (propertyManagerOwner != null && propertyManagerOwner.PropertyManager != null)
                {
                    try
                    {
                        propertyManagerOwner.PropertyManager.SetupWorkflowThread();
                    }
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                        {
                            throw;
                        }

                        setupOrCleanupException = e;
                    }
                }

                if (setupOrCleanupException == null)
                {
                    if (!workItem.Execute(this, this.SerializedBookmarkManager))
                    {
                        return Scheduler.YieldSilently;
                    }
                }
            }
            finally
            {
                // We might be multi-threaded when we execute code in this finally block. The work
                // item might have gone async and may already have called back into FinishWorkItem.
                if (propertyManagerOwner != null && propertyManagerOwner.PropertyManager != null)
                {
                    // This throws only fatal exceptions
                    propertyManagerOwner.PropertyManager.CleanupWorkflowThread(ref setupOrCleanupException);
                }

                if (setupOrCleanupException != null)
                {
                    // This API must allow the runtime to be multi-threaded when it is called.
                    this.AbortWorkflowInstance(new OperationCanceledException(SR.SetupOrCleanupWorkflowThreadThrew, setupOrCleanupException));
                }
            }

            if (setupOrCleanupException != null)
            {
                // We already aborted the instance in the finally block so now we just need to
                // return early.
                return Scheduler.Continue;
            }
            return null;
        }

        // callback from scheduler to process a work item
        internal Scheduler.RequestedAction OnExecuteWorkItem(WorkItem workItem)
        {
            workItem.Release(this);

            // thunk out early if the work item is no longer valid (that is, we're not in the
            // Executing state)
            if (!workItem.IsValid)
            {
                return Scheduler.Continue;
            }

            if (!workItem.IsEmpty)
            {
                // The try/catch/finally block used in executing a workItem prevents ryujit from
                // performing some optimizations. Moving the functionality back into this method may
                // cause a performance regression.
                var result = this.TryExecuteNonEmptyWorkItem(workItem);
                if (result != null)
                {
                    return result;
                }
            }

            if (workItem.WorkflowAbortException != null)
            {
                this.AbortWorkflowInstance(new OperationCanceledException(SR.WorkItemAbortedInstance, workItem.WorkflowAbortException));
                return Scheduler.Continue;
            }

            // We only check this in the sync path because there are no ways of changing the keys
            // collections from the work items that can go async. There's an assert to this effect
            // in FinishWorkItem.
            if (this.RawBookmarkScopeManager != null && this.RawBookmarkScopeManager.HasKeysToUpdate)
            {
                if (!workItem.FlushBookmarkScopeKeys(this))
                {
                    return Scheduler.YieldSilently;
                }

                if (workItem.WorkflowAbortException != null)
                {
                    this.AbortWorkflowInstance(new OperationCanceledException(SR.WorkItemAbortedInstance, workItem.WorkflowAbortException));
                    return Scheduler.Continue;
                }
            }

            workItem.PostProcess(this);

            if (workItem.ExceptionToPropagate != null)
            {
                this.PropagateException(workItem);
            }

            if (this.HasPendingTrackingRecords)
            {
                if (!workItem.FlushTracking(this))
                {
                    return Scheduler.YieldSilently;
                }

                if (workItem.WorkflowAbortException != null)
                {
                    this.AbortWorkflowInstance(new OperationCanceledException(SR.TrackingRelatedWorkflowAbort, workItem.WorkflowAbortException));
                    return Scheduler.Continue;
                }
            }

            this.ScheduleRuntimeWorkItems();

            if (workItem.ExceptionToPropagate != null)
            {
                this.ExitNoPersistForExceptionPropagation();
                return Scheduler.CreateNotifyUnhandledExceptionAction(workItem.ExceptionToPropagate, workItem.OriginalExceptionSource);
            }

            return Scheduler.Continue;
        }

        internal IAsyncResult BeginAssociateKeys(ICollection<InstanceKey> keysToAssociate, AsyncCallback callback, object state) => new AssociateKeysAsyncResult(this, keysToAssociate, callback, state);

        internal void EndAssociateKeys(IAsyncResult result) => AssociateKeysAsyncResult.End(result);

        internal void DisassociateKeys(ICollection<InstanceKey> keysToDisassociate) => this._host.OnDisassociateKeys(keysToDisassociate);

        internal void FinishWorkItem(WorkItem workItem)
        {
            var resumptionAction = Scheduler.Continue;

            try
            {
                Fx.Assert(this.RawBookmarkScopeManager == null || !this.RawBookmarkScopeManager.HasKeysToUpdate,
                    "FinishWorkItem should be called after FlushBookmarkScopeKeys, or by a WorkItem that could not possibly generate keys.");

                if (workItem.WorkflowAbortException != null)
                {
                    // We resume the scheduler even after abort to make sure that the proper events
                    // are raised.
                    this.AbortWorkflowInstance(new OperationCanceledException(SR.WorkItemAbortedInstance, workItem.WorkflowAbortException));
                }
                else
                {
                    workItem.PostProcess(this);

                    if (workItem.ExceptionToPropagate != null)
                    {
                        this.PropagateException(workItem);
                    }

                    if (this.HasPendingTrackingRecords)
                    {
                        if (!workItem.FlushTracking(this))
                        {
                            // We exit early here and will come back in at FinishWorkItemAfterTracking
                            resumptionAction = Scheduler.YieldSilently;
                            return;
                        }
                    }

                    if (workItem.WorkflowAbortException != null)
                    {
                        // We resume the scheduler even after abort to make sure that the proper
                        // events are raised.
                        this.AbortWorkflowInstance(new OperationCanceledException(SR.TrackingRelatedWorkflowAbort, workItem.WorkflowAbortException));
                    }
                    else
                    {
                        this.ScheduleRuntimeWorkItems();

                        if (workItem.ExceptionToPropagate != null)
                        {
                            this.ExitNoPersistForExceptionPropagation();
                            resumptionAction = Scheduler.CreateNotifyUnhandledExceptionAction(workItem.ExceptionToPropagate, workItem.OriginalExceptionSource);
                        }
                    }
                }
            }
            finally
            {
                if (resumptionAction != Scheduler.YieldSilently)
                {
                    workItem.Dispose(this);
                }
            }

            Fx.Assert(resumptionAction != Scheduler.YieldSilently, "should not reach this section if we've yielded earlier");
            this.SerializedScheduler.InternalResume(resumptionAction);
        }

        internal void FinishWorkItemAfterTracking(WorkItem workItem)
        {
            var resumptionAction = Scheduler.Continue;

            try
            {
                if (workItem.WorkflowAbortException != null)
                {
                    // We resume the scheduler even after abort to make sure that the proper events
                    // are raised.
                    this.AbortWorkflowInstance(new OperationCanceledException(SR.TrackingRelatedWorkflowAbort, workItem.WorkflowAbortException));
                }
                else
                {
                    this.ScheduleRuntimeWorkItems();

                    if (workItem.ExceptionToPropagate != null)
                    {
                        this.ExitNoPersistForExceptionPropagation();
                        resumptionAction = Scheduler.CreateNotifyUnhandledExceptionAction(workItem.ExceptionToPropagate, workItem.OriginalExceptionSource);
                    }
                }
            }
            finally
            {
                workItem.Dispose(this);
            }

            this.SerializedScheduler.InternalResume(resumptionAction);
        }

        private void ScheduleRuntimeWorkItems()
        {
            if (this._runtimeTransaction != null && this._runtimeTransaction.ShouldScheduleCompletion)
            {
                this.SerializedScheduler.PushWork(new CompleteTransactionWorkItem(this._runtimeTransaction.IsolationScope));
                return;
            }

            if (this._persistenceWaiters != null && this._persistenceWaiters.Count > 0 &&
                this.IsPersistable)
            {
                var waiter = this._persistenceWaiters.Dequeue();

                while (waiter != null && waiter.WaitingInstance.IsCompleted)
                {
                    // We just skip completed instance so we don't have to deal with the
                    // housekeeping are arbitrary removal from our queue

                    if (this._persistenceWaiters.Count == 0)
                    {
                        waiter = null;
                    }
                    else
                    {
                        waiter = this._persistenceWaiters.Dequeue();
                    }
                }

                if (waiter != null)
                {
                    this.SerializedScheduler.PushWork(waiter.CreateWorkItem());
                    return;
                }
            }
        }

        internal void AbortActivityInstance(ActivityInstance instance, Exception reason)
        {
            instance.Abort(this, this.SerializedBookmarkManager, reason, true);

            if (instance.CompletionBookmark != null)
            {
                instance.CompletionBookmark.CheckForCancelation();
            }
            else if (instance.Parent != null)
            {
                instance.CompletionBookmark = new CompletionBookmark();
            }

            this.ScheduleCompletionBookmark(instance);
        }

        internal Exception CompleteActivityInstance(ActivityInstance targetInstance)
        {
            Exception exceptionToPropagate = null;

            // 1. Handle any root related work
            this.HandleRootCompletion(targetInstance);

            // 2. Schedule the completion bookmark We MUST schedule the completion bookmark before
            // we dispose the environment because we take this opportunity to gather up any output values.
            this.ScheduleCompletionBookmark(targetInstance);

            if (!targetInstance.HasNotExecuted)
            {
                this.DebugActivityCompleted(targetInstance);
            }

            // 3. Cleanup environmental resources (properties, handles, mapped locations)
            try
            {
                if (targetInstance.PropertyManager != null)
                {
                    targetInstance.PropertyManager.UnregisterProperties(targetInstance, targetInstance.Activity.MemberOf);
                }

                if (this.IsSecondaryRoot(targetInstance))
                {
                    // We need to appropriately remove references, dispose environments, and remove
                    // instance map entries for all environments in this chain
                    var environment = targetInstance.Environment;

                    if (targetInstance.IsEnvironmentOwner)
                    {
                        environment.RemoveReference(true);

                        if (environment.ShouldDispose)
                        {
                            // Unintialize all handles declared in this environment.
                            environment.UninitializeHandles(targetInstance);

                            environment.Dispose();
                        }

                        environment = environment.Parent;
                    }

                    while (environment != null)
                    {
                        environment.RemoveReference(false);

                        if (environment.ShouldDispose)
                        {
                            // Unintialize all handles declared in this environment.
                            environment.UninitializeHandles(targetInstance);

                            environment.Dispose();

                            // This also implies that the owner is complete so we should remove it
                            // from the map
                            if (this._instanceMap != null)
                            {
                                this._instanceMap.RemoveEntry(environment);
                            }
                        }

                        environment = environment.Parent;
                    }
                }
                else if (targetInstance.IsEnvironmentOwner)
                {
                    targetInstance.Environment.RemoveReference(true);

                    if (targetInstance.Environment.ShouldDispose)
                    {
                        // Unintialize all handles declared in this environment.
                        targetInstance.Environment.UninitializeHandles(targetInstance);

                        targetInstance.Environment.Dispose();
                    }
                    else if (this._instanceMap != null)
                    {
                        // Someone else is referencing this environment Note that we don't use
                        // TryAdd since no-one else should have added it before.
                        this._instanceMap.AddEntry(targetInstance.Environment);
                    }
                }
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }

                exceptionToPropagate = e;
            }

            // 4. Cleanup remaining instance related resources (bookmarks, program mapping)
            targetInstance.MarkAsComplete(this.RawBookmarkScopeManager, this.SerializedBookmarkManager);

            // 5. Track our final state
            targetInstance.FinalizeState(this, exceptionToPropagate != null);

            return exceptionToPropagate;
        }

        internal bool TryGetPendingOperation(ActivityInstance instance, out AsyncOperationContext asyncContext)
        {
            if (this._activeOperations != null)
            {
                return this._activeOperations.TryGetValue(instance, out asyncContext);
            }
            else
            {
                asyncContext = null;
                return false;
            }
        }

        internal void CancelPendingOperation(ActivityInstance instance)
        {
            if (this.TryGetPendingOperation(instance, out var asyncContext))
            {
                if (asyncContext.IsStillActive)
                {
                    asyncContext.CancelOperation();
                }
            }
        }

        internal void HandleRootCompletion(ActivityInstance completedInstance)
        {
            if (completedInstance.Parent == null)
            {
                if (completedInstance == this.SerializedRootInstance)
                {
                    this.SerializedShouldRaiseMainBodyComplete = true;

                    Fx.Assert(this.SerializedExecutionState == ActivityInstanceState.Executing, "We shouldn't have a guess at our completion state yet.");

                    // We start by assuming our completion state will match the root instance.
                    this.SerializedExecutionState = this.SerializedRootInstance.State;
                    this.SerializedRootEnvironment = this.SerializedRootInstance.Environment;
                }
                else
                {
                    Fx.Assert(this._executingSecondaryRootInstances.Contains(completedInstance), "An instance which is not the main root and doesn't have an execution parent must be an executing secondary root.");
                    this._executingSecondaryRootInstances.Remove(completedInstance);
                }

                // We just had a root complete, let's see if we're all the way done and should
                // gather outputs from the root. Note that we wait until everything completes in
                // case the root environment was detached.
                if (this.SerializedRootInstance.IsCompleted
                    && (this._executingSecondaryRootInstances == null || this._executingSecondaryRootInstances.Count == 0))
                {
                    this.GatherRootOutputs();

                    // uninitialize any host-provided handles
                    if (this.RootPropertyManager != null)
                    {
                        // and uninitialize host-provided handles
                        var context = new HandleInitializationContext(this, null);
                        foreach (var executionProperty in this.RootPropertyManager.Properties.Values)
                        {
                            if (executionProperty.Property is Handle handle)
                            {
                                handle.Uninitialize(context);
                            }
                        }
                        context.Dispose();

                        // unregister any properties that were registered
                        this.RootPropertyManager.UnregisterProperties(null, null);
                    }
                }
            }
        }

        private bool IsSecondaryRoot(ActivityInstance instance) => instance.Parent == null && instance != this.SerializedRootInstance;

        private void GatherRootOutputs()
        {
            Fx.Assert(this.SerializedWorkflowOutputs == null, "We should only get workflow outputs when we actually complete which should only happen once.");
            Fx.Assert(ActivityUtilities.IsCompletedState(this.SerializedRootInstance.State), "We should only gather outputs when in a completed state.");
            Fx.Assert(this.SerializedRootEnvironment != null, "We should have set the root environment");

            // We only gather outputs for Closed - not for canceled or faulted
            if (this.SerializedRootInstance.State == ActivityInstanceState.Closed)
            {
                // We use rootElement here instead of _rootInstance.Activity because we don't always
                // reload the root instance (like if it was complete when we last persisted).
                var rootArguments = this.RootActivity.RuntimeArguments;

                for (var i = 0; i < rootArguments.Count; i++)
                {
                    var argument = rootArguments[i];

                    if (ArgumentDirectionHelper.IsOut(argument.Direction))
                    {
                        if (this.SerializedWorkflowOutputs == null)
                        {
                            this.SerializedWorkflowOutputs = new Dictionary<string, object>();
                        }

                        var location = this.SerializedRootEnvironment.GetSpecificLocation(argument.BoundArgument.Id);
                        if (location == null)
                        {
                            throw FxTrace.Exception.AsError(new InvalidOperationException(SR.NoOutputLocationWasFound(argument.Name)));
                        }
                        this.SerializedWorkflowOutputs.Add(argument.Name, location.Value);
                    }
                }
            }

            // GatherRootOutputs only ever gets called once so we can null it out the root
            // environment now.
            this.SerializedRootEnvironment = null;
        }

        internal void NotifyUnhandledException(Exception exception, ActivityInstance source)
        {
            try
            {
                this._host.NotifyUnhandledException(exception, source.Activity, source.Id);
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }
                this.AbortWorkflowInstance(e);
            }
        }

        internal void OnSchedulerIdle()
        {
            // If we're terminating we'll call terminate here and then do the normal notification
            // for the host.
            if (this.IsTerminatePending)
            {
                Fx.Assert(this._terminationPendingException != null, "Should have set terminationPendingException at the same time that we set isTerminatePending = true");
                if (this._terminationPendingException == null)
                {
                    throw new NullReferenceException("Should have set terminationPendingException at the same time that we set isTerminatePending = true");
                }

                this.Terminate(this._terminationPendingException);
                this.IsTerminatePending = false;
            }

            if (this.IsIdle)
            {
                if (this._transactionContextWaiters != null && this._transactionContextWaiters.Count > 0)
                {
                    if (this.IsPersistable || (this._transactionContextWaiters[0].IsRequires && this._noPersistCount == 1))
                    {
                        var waiter = this._transactionContextWaiters.Dequeue();

                        waiter.WaitingInstance.DecrementBusyCount();
                        waiter.WaitingInstance.WaitingForTransactionContext = false;

                        this.ScheduleItem(new TransactionContextWorkItem(waiter));

                        this.MarkSchedulerRunning();
                        this.ResumeScheduler();

                        return;
                    }
                }

                if (this.SerializedShouldRaiseMainBodyComplete)
                {
                    this.SerializedShouldRaiseMainBodyComplete = false;
                    if (this.SerializedMainRootCompleteBookmark != null)
                    {
                        var resumptionResult = this.TryResumeUserBookmark(this.SerializedMainRootCompleteBookmark, this.SerializedRootInstance.State, false);
                        this.SerializedMainRootCompleteBookmark = null;
                        if (resumptionResult == BookmarkResumptionResult.Success)
                        {
                            this.MarkSchedulerRunning();
                            this.ResumeScheduler();
                            return;
                        }
                    }

                    if (this._executingSecondaryRootInstances == null || this._executingSecondaryRootInstances.Count == 0)
                    {
                        // if we got to this point we're completely done from the executor's point
                        // of view. outputs have been gathered, no more work is happening. Clear out
                        // some fields to shrink our "completed instance" persistence size
                        this.Dispose(false);
                    }
                }
            }

            if (this._shouldPauseOnCanPersist && this.IsPersistable)
            {
                this._shouldPauseOnCanPersist = false;
            }

            try
            {
                this._host.NotifyPaused();
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }
                this.AbortWorkflowInstance(e);
            }
        }

        public void Open(SynchronizationContext synchronizationContext) => this.SerializedScheduler.Open(synchronizationContext);

        public void PauseScheduler()
        {
            // Since we don't require calls to WorkflowInstanceControl.Pause to be synchronized by
            // the caller, we need to check for null here
            var localScheduler = this.SerializedScheduler;

            if (localScheduler != null)
            {
                localScheduler.Pause();
            }
        }

        public object PrepareForSerialization()
        {
            if (this._host.HasTrackingParticipant)
            {
                this.SerializedNextTrackingRecordNumber = this._host.TrackingProvider.NextTrackingRecordNumber;
                this.SerializedHasTrackedStarted = this._host.HasTrackedStarted;
            }
            return this;
        }

        public void RequestPersist(Bookmark onPersistBookmark, ActivityInstance requestingInstance)
        {
            if (this._persistenceWaiters == null)
            {
                this._persistenceWaiters = new Queue<PersistenceWaiter>();
            }

            this._persistenceWaiters.Enqueue(new PersistenceWaiter(onPersistBookmark, requestingInstance));
        }

        private void ScheduleCompletionBookmark(ActivityInstance completedInstance)
        {
            if (completedInstance.CompletionBookmark != null)
            {
                this.SerializedScheduler.PushWork(completedInstance.CompletionBookmark.GenerateWorkItem(completedInstance, this));
            }
            else if (completedInstance.Parent != null)
            {
                // Variable defaults and argument expressions always have a parent and never have a CompletionBookmark
                if (completedInstance.State != ActivityInstanceState.Closed && completedInstance.Parent.HasNotExecuted)
                {
                    completedInstance.Parent.SetInitializationIncomplete();
                }

                this.SerializedScheduler.PushWork(this.CreateEmptyWorkItem(completedInstance.Parent));
            }
        }

        // This method is called by WorkflowInstance - these are bookmark resumptions originated by
        // the host
        internal BookmarkResumptionResult TryResumeHostBookmark(Bookmark bookmark, object value)
        {
            var hasTracedResume = this.TryTraceResume(out var oldActivityId);

            var result = this.TryResumeUserBookmark(bookmark, value, true);

            this.TraceSuspend(hasTracedResume, oldActivityId);

            return result;
        }

        internal BookmarkResumptionResult TryResumeUserBookmark(Bookmark bookmark, object value, bool isExternal)
        {
            if (this._isDisposed)
            {
                return BookmarkResumptionResult.NotFound;
            }

            ActivityInstance isolationInstance = null;

            if (this._runtimeTransaction != null)
            {
                isolationInstance = this._runtimeTransaction.IsolationScope;
            }

            var result = this.SerializedBookmarkManager.TryGenerateWorkItem(this, isExternal, ref bookmark, value, isolationInstance, out var resumeExecutionWorkItem);

            if (result == BookmarkResumptionResult.Success)
            {
                this.SerializedScheduler.EnqueueWork(resumeExecutionWorkItem);

                if (this.ShouldTrackBookmarkResumptionRecords)
                {
                    this.AddTrackingRecord(new BookmarkResumptionRecord(this.WorkflowInstanceId, bookmark, resumeExecutionWorkItem.ActivityInstance, value));
                }
            }
            else if (result == BookmarkResumptionResult.NotReady)
            {
                // We had the bookmark but this is not an appropriate time to resume it so we won't
                // do anything here
            }
            else if (bookmark == Bookmark.AsyncOperationCompletionBookmark)
            {
                Fx.Assert(result == BookmarkResumptionResult.NotFound, "This BookmarkNotFound is actually a well-known bookmark.");

                var data = (AsyncOperationContext.CompleteData)value;

                data.CompleteOperation();

                result = BookmarkResumptionResult.Success;
            }

            return result;
        }

        internal ReadOnlyCollection<BookmarkInfo> GetAllBookmarks()
        {
            var bookmarks = this.CollectExternalBookmarks();

            if (bookmarks != null)
            {
                return new ReadOnlyCollection<BookmarkInfo>(bookmarks);
            }
            else
            {
                return EmptyBookmarkInfoCollection;
            }
        }

        private List<BookmarkInfo> CollectExternalBookmarks()
        {
            List<BookmarkInfo> bookmarks = null;

            if (this.SerializedBookmarkManager != null && this.SerializedBookmarkManager.HasBookmarks)
            {
                bookmarks = new List<BookmarkInfo>();

                this.SerializedBookmarkManager.PopulateBookmarkInfo(bookmarks);
            }

            if (this.RawBookmarkScopeManager != null)
            {
                this.RawBookmarkScopeManager.PopulateBookmarkInfo(ref bookmarks);
            }

            if (bookmarks == null || bookmarks.Count == 0)
            {
                return null;
            }
            else
            {
                return bookmarks;
            }
        }

        internal ReadOnlyCollection<BookmarkInfo> GetBookmarks(BookmarkScope scope)
        {
            if (this.RawBookmarkScopeManager == null)
            {
                return EmptyBookmarkInfoCollection;
            }
            else
            {
                var bookmarks = this.RawBookmarkScopeManager.GetBookmarks(scope);

                if (bookmarks == null)
                {
                    return EmptyBookmarkInfoCollection;
                }
                else
                {
                    return bookmarks;
                }
            }
        }

        internal IAsyncResult BeginResumeBookmark(Bookmark bookmark, object value, TimeSpan timeout, AsyncCallback callback, object state) => this._host.OnBeginResumeBookmark(bookmark, value, timeout, callback, state);

        internal BookmarkResumptionResult EndResumeBookmark(IAsyncResult result) => this._host.OnEndResumeBookmark(result);

        // This is only called by WorkflowInstance so it behaves like TryResumeUserBookmark with
        // must run work item set to true
        internal BookmarkResumptionResult TryResumeBookmark(Bookmark bookmark, object value, BookmarkScope scope)
        {
            // We have to perform all of this work with tracing set up since we might initialize a
            // sub-instance while generating the work item.
            var hasTracedResume = this.TryTraceResume(out var oldActivityId);

            ActivityInstance isolationInstance = null;

            if (this._runtimeTransaction != null)
            {
                isolationInstance = this._runtimeTransaction.IsolationScope;
            }

            var hasOperations = this._activeOperations != null && this._activeOperations.Count > 0;

            var result = this.BookmarkScopeManager.TryGenerateWorkItem(this, ref bookmark, scope, value, isolationInstance, hasOperations || this.SerializedBookmarkManager.HasBookmarks, out var resumeExecutionWorkItem);

            if (result == BookmarkResumptionResult.Success)
            {
                this.SerializedScheduler.EnqueueWork(resumeExecutionWorkItem);

                if (this.ShouldTrackBookmarkResumptionRecords)
                {
                    this.AddTrackingRecord(new BookmarkResumptionRecord(this.WorkflowInstanceId, bookmark, resumeExecutionWorkItem.ActivityInstance, value));
                }
            }

            this.TraceSuspend(hasTracedResume, oldActivityId);

            return result;
        }

        public void MarkSchedulerRunning() => this.SerializedScheduler.MarkRunning();

        public void Run() => this.ResumeScheduler();

        private void ResumeScheduler() => this.SerializedScheduler.Resume();

        internal void ScheduleItem(WorkItem workItem) => this.SerializedScheduler.PushWork(workItem);

        public void ScheduleRootActivity(Activity activity, IDictionary<string, object> argumentValueOverrides, IList<Handle> hostProperties)
        {
            Fx.Assert(this.SerializedRootInstance == null, "ScheduleRootActivity should only be called once");

            if (hostProperties != null && hostProperties.Count > 0)
            {
                var rootProperties = new Dictionary<string, ExecutionPropertyManager.ExecutionProperty>(hostProperties.Count);
                var context = new HandleInitializationContext(this, null);
                for (var i = 0; i < hostProperties.Count; i++)
                {
                    var handle = hostProperties[i];
                    handle.Initialize(context);
                    rootProperties.Add(handle.ExecutionPropertyName, new ExecutionPropertyManager.ExecutionProperty(handle.ExecutionPropertyName, handle, null));
                }
                context.Dispose();

                this.RootPropertyManager = new ExecutionPropertyManager(null, rootProperties);
            }

            var hasTracedStart = this.TryTraceStart(out var oldActivityId);

            // Create and initialize the root instance
            this.SerializedRootInstance = new ActivityInstance(activity)
            {
                PropertyManager = RootPropertyManager
            };
            this.RootActivity = activity;

            Fx.Assert(this.SerializedLastInstanceId == 0, "We should only hit this path once");
            this.SerializedLastInstanceId++;

            var requiresSymbolResolution = this.SerializedRootInstance.Initialize(null, this._instanceMap, null, this.SerializedLastInstanceId, this);

            if (TD.ActivityScheduledIsEnabled())
            {
                this.TraceActivityScheduled(null, activity, this.SerializedRootInstance.Id);
            }

            // Add the work item for executing the root
            this.SerializedScheduler.PushWork(new ExecuteRootWorkItem(this.SerializedRootInstance, requiresSymbolResolution, argumentValueOverrides));

            this.TraceSuspend(hasTracedStart, oldActivityId);
        }

        public void RegisterMainRootCompleteCallback(Bookmark bookmark) => this.SerializedMainRootCompleteBookmark = bookmark;

        public ActivityInstance ScheduleSecondaryRootActivity(Activity activity, LocationEnvironment environment)
        {
            var secondaryRoot = this.ScheduleActivity(activity, null, null, null, environment);

            while (environment != null)
            {
                environment.AddReference();
                environment = environment.Parent;
            }

            if (this._executingSecondaryRootInstances == null)
            {
                this._executingSecondaryRootInstances = new List<ActivityInstance>();
            }

            this._executingSecondaryRootInstances.Add(secondaryRoot);

            return secondaryRoot;
        }

        public ActivityInstance ScheduleActivity(Activity activity, ActivityInstance parent,
            CompletionBookmark completionBookmark, FaultBookmark faultBookmark, LocationEnvironment parentEnvironment) => this.ScheduleActivity(activity, parent, completionBookmark, faultBookmark, parentEnvironment, null, null);

        public ActivityInstance ScheduleDelegate(ActivityDelegate activityDelegate, IDictionary<string, object> inputParameters, ActivityInstance parent, LocationEnvironment executionEnvironment,
            CompletionBookmark completionBookmark, FaultBookmark faultBookmark)
        {
            Fx.Assert(activityDelegate.Owner != null, "activityDelegate must have an owner");
            Fx.Assert(parent != null, "activityDelegate should have a parent activity instance");

            ActivityInstance handlerInstance;

            if (activityDelegate.Handler == null)
            {
                handlerInstance = ActivityInstance.CreateCompletedInstance(new EmptyDelegateActivity());
                handlerInstance.CompletionBookmark = completionBookmark;
                this.ScheduleCompletionBookmark(handlerInstance);
            }
            else
            {
                handlerInstance = this.CreateUninitalizedActivityInstance(activityDelegate.Handler, parent, completionBookmark, faultBookmark);
                var requiresSymbolResolution = handlerInstance.Initialize(parent, this._instanceMap, executionEnvironment, this.SerializedLastInstanceId, this, activityDelegate.RuntimeDelegateArguments.Count);

                var activityDelegateParameters = activityDelegate.RuntimeDelegateArguments;
                for (var i = 0; i < activityDelegateParameters.Count; i++)
                {
                    var runtimeArgument = activityDelegateParameters[i];

                    if (runtimeArgument.BoundArgument != null)
                    {
                        var delegateParameterName = runtimeArgument.Name;

                        // Populate argument location. Set it's value in the activity handler's
                        // instance environment only if it is a DelegateInArgument.
                        var newLocation = runtimeArgument.BoundArgument.CreateLocation();
                        handlerInstance.Environment.Declare(runtimeArgument.BoundArgument, newLocation, handlerInstance);

                        if (ArgumentDirectionHelper.IsIn(runtimeArgument.Direction))
                        {
                            if (inputParameters != null && inputParameters.Count > 0)
                            {
                                newLocation.Value = inputParameters[delegateParameterName];
                            }
                        }
                    }
                }

                if (TD.ActivityScheduledIsEnabled())
                {
                    this.TraceActivityScheduled(parent, activityDelegate.Handler, handlerInstance.Id);
                }

                if (this.ShouldTrackActivityScheduledRecords)
                {
                    this.AddTrackingRecord(new ActivityScheduledRecord(this.WorkflowInstanceId, parent, handlerInstance));
                }

                this.ScheduleBody(handlerInstance, requiresSymbolResolution, null, null);
            }

            return handlerInstance;
        }

        private void TraceActivityScheduled(ActivityInstance parent, Activity activity, string scheduledInstanceId)
        {
            Fx.Assert(TD.ActivityScheduledIsEnabled(), "This should be checked before calling this helper.");

            if (parent != null)
            {
                TD.ActivityScheduled(parent.Activity.GetType().ToString(), parent.Activity.DisplayName, parent.Id, activity.GetType().ToString(), activity.DisplayName, scheduledInstanceId);
            }
            else
            {
                TD.ActivityScheduled(string.Empty, string.Empty, string.Empty, activity.GetType().ToString(), activity.DisplayName, scheduledInstanceId);
            }
        }

        private ActivityInstance CreateUninitalizedActivityInstance(Activity activity, ActivityInstance parent, CompletionBookmark completionBookmark, FaultBookmark faultBookmark)
        {
            Fx.Assert(activity.IsMetadataCached, "Metadata must be cached for us to process this activity.");

            // 1. Create a new activity instance and setup bookmark callbacks
            var activityInstance = new ActivityInstance(activity);

            if (parent != null)
            {
                // add a bookmarks to complete at activity.Close/Fault time
                activityInstance.CompletionBookmark = completionBookmark;
                activityInstance.FaultBookmark = faultBookmark;
                parent.AddChild(activityInstance);
            }

            // 2. Setup parent and environment machinery, and add to instance's program mapping for
            // persistence (if necessary)
            this.IncrementLastInstanceId();

            return activityInstance;
        }

        private void IncrementLastInstanceId()
        {
            if (this.SerializedLastInstanceId == long.MaxValue)
            {
                throw FxTrace.Exception.AsError(new NotSupportedException(SR.OutOfInstanceIds));
            }
            this.SerializedLastInstanceId++;
        }

        private ActivityInstance ScheduleActivity(Activity activity, ActivityInstance parent,
            CompletionBookmark completionBookmark, FaultBookmark faultBookmark, LocationEnvironment parentEnvironment,
            IDictionary<string, object> argumentValueOverrides, Location resultLocation)
        {
            var activityInstance = this.CreateUninitalizedActivityInstance(activity, parent, completionBookmark, faultBookmark);
            var requiresSymbolResolution = activityInstance.Initialize(parent, this._instanceMap, parentEnvironment, this.SerializedLastInstanceId, this);

            if (TD.ActivityScheduledIsEnabled())
            {
                this.TraceActivityScheduled(parent, activity, activityInstance.Id);
            }

            if (this.ShouldTrackActivityScheduledRecords)
            {
                this.AddTrackingRecord(new ActivityScheduledRecord(this.WorkflowInstanceId, parent, activityInstance));
            }

            this.ScheduleBody(activityInstance, requiresSymbolResolution, argumentValueOverrides, resultLocation);

            return activityInstance;
        }

        internal void ScheduleExpression(ActivityWithResult activity, ActivityInstance parent, LocationEnvironment parentEnvironment, Location resultLocation, ResolveNextArgumentWorkItem nextArgumentWorkItem)
        {
            Fx.Assert(resultLocation != null, "We should always schedule expressions with a result location.");

            if (!activity.IsMetadataCached || activity.CacheId != parent.Activity.CacheId)
            {
                throw FxTrace.Exception.Argument(nameof(activity), SR.ActivityNotPartOfThisTree(activity.DisplayName, parent.Activity.DisplayName));
            }

            if (activity.SkipArgumentResolution)
            {
                Fx.Assert(!activity.UseOldFastPath || parent.SubState == ActivityInstance.Substate.Executing,
                    "OldFastPath activities should have been handled by the Populate methods, unless this is a dynamic update");

                this.IncrementLastInstanceId();

                this.ScheduleExpression(activity, parent, resultLocation, nextArgumentWorkItem, this.SerializedLastInstanceId);
            }
            else
            {
                if (nextArgumentWorkItem != null)
                {
                    this.ScheduleItem(nextArgumentWorkItem);
                }
                this.ScheduleActivity(activity, parent, null, null, parentEnvironment, null, resultLocation.CreateReference(true));
            }
        }

        private void ScheduleExpression(ActivityWithResult activity, ActivityInstance parent, Location resultLocation, ResolveNextArgumentWorkItem nextArgumentWorkItem, long instanceId)
        {
            if (TD.ActivityScheduledIsEnabled())
            {
                this.TraceActivityScheduled(parent, activity, instanceId.ToString(CultureInfo.InvariantCulture));
            }

            if (this.ShouldTrackActivityScheduledRecords)
            {
                this.AddTrackingRecord(new ActivityScheduledRecord(this.WorkflowInstanceId, parent, new ActivityInfo(activity, instanceId)));
            }

            var workItem = this.ExecuteSynchronousExpressionWorkItemPool.Acquire();
            workItem.Initialize(parent, activity, this.SerializedLastInstanceId, resultLocation, nextArgumentWorkItem);
            if (this._instanceMap != null)
            {
                this._instanceMap.AddEntry(workItem);
            }
            this.ScheduleItem(workItem);
        }

        internal void ScheduleExpressionFaultPropagation(Activity activity, long instanceId, ActivityInstance parent, Exception exception)
        {
            var instance = new ActivityInstance(activity);
            instance.Initialize(parent, this._instanceMap, parent.Environment, instanceId, this);

            if (!parent.HasPendingWork)
            {
                // Force the parent to stay alive, and to attempt to execute its body if the fault
                // is handled
                this.ScheduleItem(this.CreateEmptyWorkItem(parent));
            }
            var workItem = new PropagateExceptionWorkItem(exception, instance);
            this.ScheduleItem(workItem);

            parent.SetInitializationIncomplete();
        }

        // Argument and variables resolution for root activity is defered to execution time
        // invocation of this method means that we're ready to schedule Activity.Execute()
        internal void ScheduleBody(ActivityInstance activityInstance, bool requiresSymbolResolution,
            IDictionary<string, object> argumentValueOverrides, Location resultLocation)
        {
            if (resultLocation == null)
            {
                var workItem = this.ExecuteActivityWorkItemPool.Acquire();
                workItem.Initialize(activityInstance, requiresSymbolResolution, argumentValueOverrides);

                this.SerializedScheduler.PushWork(workItem);
            }
            else
            {
                this.SerializedScheduler.PushWork(new ExecuteExpressionWorkItem(activityInstance, requiresSymbolResolution, argumentValueOverrides, resultLocation));
            }
        }

        public NoPersistProperty CreateNoPersistProperty() => new NoPersistProperty(this);

        public AsyncOperationContext SetupAsyncOperationBlock(ActivityInstance owningActivity)
        {
            if (this._activeOperations != null && this._activeOperations.ContainsKey(owningActivity))
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.OnlyOneOperationPerActivity));
            }

            this.EnterNoPersist();

            var context = new AsyncOperationContext(this, owningActivity);

            if (this._activeOperations == null)
            {
                this._activeOperations = new Dictionary<ActivityInstance, AsyncOperationContext>();
            }

            this._activeOperations.Add(owningActivity, context);

            return context;
        }

        // Must always be called from a workflow thread
        public void CompleteOperation(ActivityInstance owningInstance, BookmarkCallback callback, object state)
        {
            Fx.Assert(callback != null, "Use the other overload if callback is null.");

            var workItem = new CompleteAsyncOperationWorkItem(
                new BookmarkCallbackWrapper(callback, owningInstance),
                this.SerializedBookmarkManager.GenerateTempBookmark(),
                state);
            this.CompleteOperation(workItem);
        }

        // Must always be called from a workflow thread
        public void CompleteOperation(WorkItem asyncCompletionWorkItem)
        {
            this.SerializedScheduler.EnqueueWork(asyncCompletionWorkItem);
            this.CompleteOperation(asyncCompletionWorkItem.ActivityInstance, false);
        }

        // Must always be called from a workflow thread
        public void CompleteOperation(ActivityInstance owningInstance) => this.CompleteOperation(owningInstance, true);

        private void CompleteOperation(ActivityInstance owningInstance, bool exitNoPersist)
        {
            Fx.Assert(owningInstance != null, "Cannot be called with a null instance.");
            Fx.Assert(this._activeOperations.ContainsKey(owningInstance), "The owning instance must be in the list if we've gotten here.");

            this._activeOperations.Remove(owningInstance);

            owningInstance.DecrementBusyCount();

            if (exitNoPersist)
            {
                this.ExitNoPersist();
            }
        }

        internal void AddHandle(Handle handleToAdd)
        {
            if (this.Handles == null)
            {
                this.Handles = new List<Handle>();
            }
            this.Handles.Add(handleToAdd);
        }

        [DataContract]
        internal class PersistenceWaiter
        {
            public PersistenceWaiter(Bookmark onPersist, ActivityInstance waitingInstance)
            {
                this.OnPersistBookmark = onPersist;
                this.WaitingInstance = waitingInstance;
            }

            public Bookmark OnPersistBookmark { get; private set; }

            public ActivityInstance WaitingInstance { get; private set; }

            [DataMember(Name = "OnPersistBookmark")]
            internal Bookmark SerializedOnPersistBookmark
            {
                get { return this.OnPersistBookmark; }
                set { this.OnPersistBookmark = value; }
            }

            [DataMember(Name = "WaitingInstance")]
            internal ActivityInstance SerializedWaitingInstance
            {
                get { return this.WaitingInstance; }
                set { this.WaitingInstance = value; }
            }

            public WorkItem CreateWorkItem() => new PersistWorkItem(this);

            [DataContract]
            internal class PersistWorkItem : WorkItem
            {
                public PersistWorkItem(PersistenceWaiter waiter)
                    : base(waiter.WaitingInstance)
                {
                    this.SerializedWaiter = waiter;
                }

                public override bool IsValid => true;

                public override ActivityInstance PropertyManagerOwner =>
                        // Persist should not pick up user transaction / identity.
                        null;

                [DataMember(Name = "waiter")]
                internal PersistenceWaiter SerializedWaiter { get; set; }

                public override void TraceCompleted() => this.TraceRuntimeWorkItemCompleted();

                public override void TraceScheduled() => this.TraceRuntimeWorkItemScheduled();

                public override void TraceStarting() => this.TraceRuntimeWorkItemStarting();

                public override bool Execute(ActivityExecutor executor, BookmarkManager bookmarkManager)
                {
                    if (executor.TryResumeUserBookmark(this.SerializedWaiter.OnPersistBookmark, null, false) != BookmarkResumptionResult.Success)
                    {
                        Fx.Assert("This should always be resumable.");
                    }

                    IAsyncResult result = null;

                    try
                    {
                        result = executor._host.OnBeginPersist(Fx.ThunkCallback(new AsyncCallback(this.OnPersistComplete)), executor);

                        if (result.CompletedSynchronously)
                        {
                            executor._host.OnEndPersist(result);
                        }
                    }
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                        {
                            throw;
                        }

                        this._workflowAbortException = e;
                    }

                    return result == null || result.CompletedSynchronously;
                }

                private void OnPersistComplete(IAsyncResult result)
                {
                    if (result.CompletedSynchronously)
                    {
                        return;
                    }

                    var executor = (ActivityExecutor)result.AsyncState;

                    try
                    {
                        executor._host.OnEndPersist(result);
                    }
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                        {
                            throw;
                        }

                        this._workflowAbortException = e;
                    }

                    executor.FinishWorkItem(this);
                }

                public override void PostProcess(ActivityExecutor executor)
                {
                    if (this.ExceptionToPropagate != null)
                    {
                        executor.AbortActivityInstance(this.SerializedWaiter.WaitingInstance, this.ExceptionToPropagate);
                    }
                }
            }
        }

        [DataContract]
        internal class AbortActivityWorkItem : WorkItem
        {
            private readonly ActivityExecutor _executor;

            public AbortActivityWorkItem(ActivityExecutor executor, ActivityInstance activityInstance, Exception reason, ActivityInstanceReference originalSource)
                : base(activityInstance)
            {
                this.SerializedReason = reason;
                this.SerializedOriginalSource = originalSource;

                this.IsEmpty = true;
                this._executor = executor;
            }

            public override ActivityInstance OriginalExceptionSource => this.SerializedOriginalSource.ActivityInstance;

            public override bool IsValid => this.ActivityInstance.State == ActivityInstanceState.Executing;

            public override ActivityInstance PropertyManagerOwner
            {
                get
                {
                    Fx.Assert("This is never called.");

                    return null;
                }
            }

            [DataMember(Name = "reason")]
            internal Exception SerializedReason { get; set; }

            [DataMember(Name = "originalSource")]
            internal ActivityInstanceReference SerializedOriginalSource { get; set; }

            public override void TraceCompleted() => this.TraceRuntimeWorkItemCompleted();

            public override void TraceScheduled() => this.TraceRuntimeWorkItemScheduled();

            public override void TraceStarting() => this.TraceRuntimeWorkItemStarting();

            public override bool Execute(ActivityExecutor executor, BookmarkManager bookmarkManager)
            {
                Fx.Assert("This is never called");

                return true;
            }

            public override void PostProcess(ActivityExecutor executor)
            {
                executor.AbortActivityInstance(this.ActivityInstance, this.SerializedReason);

                // We always repropagate the exception from here
                this.ExceptionToPropagate = this.SerializedReason;

                // Tell the executor to decrement its NoPersistCount, if necessary.
                executor.ExitNoPersistForExceptionPropagation();
            }
        }

        [DataContract]
        internal class CompleteAsyncOperationWorkItem : BookmarkWorkItem
        {
            public CompleteAsyncOperationWorkItem(BookmarkCallbackWrapper wrapper, Bookmark bookmark, object value)
                : base(wrapper, bookmark, value)
            {
                this.ExitNoPersistRequired = true;
            }
        }

        [DataContract]
        internal class CancelActivityWorkItem : ActivityExecutionWorkItem
        {
            public CancelActivityWorkItem(ActivityInstance activityInstance)
                : base(activityInstance)
            {
            }

            public override void TraceCompleted()
            {
                if (TD.CompleteCancelActivityWorkItemIsEnabled())
                {
                    TD.CompleteCancelActivityWorkItem(this.ActivityInstance.Activity.GetType().ToString(), this.ActivityInstance.Activity.DisplayName, this.ActivityInstance.Id);
                }
            }

            public override void TraceScheduled()
            {
                if (TD.ScheduleCancelActivityWorkItemIsEnabled())
                {
                    TD.ScheduleCancelActivityWorkItem(this.ActivityInstance.Activity.GetType().ToString(), this.ActivityInstance.Activity.DisplayName, this.ActivityInstance.Id);
                }
            }

            public override void TraceStarting()
            {
                if (TD.StartCancelActivityWorkItemIsEnabled())
                {
                    TD.StartCancelActivityWorkItem(this.ActivityInstance.Activity.GetType().ToString(), this.ActivityInstance.Activity.DisplayName, this.ActivityInstance.Id);
                }
            }

            public override bool Execute(ActivityExecutor executor, BookmarkManager bookmarkManager)
            {
                try
                {
                    this.ActivityInstance.Cancel(executor, bookmarkManager);
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    this.ExceptionToPropagate = e;
                }

                return true;
            }
        }

        [DataContract]
        internal class ExecuteActivityWorkItem : ActivityExecutionWorkItem
        {

            // Called by the pool.
            public ExecuteActivityWorkItem()
            {
                this.IsPooled = true;
            }

            // Called by non-pool subclasses.
            protected ExecuteActivityWorkItem(ActivityInstance activityInstance, bool requiresSymbolResolution, IDictionary<string, object> argumentValueOverrides)
                : base(activityInstance)
            {
                this.SerializedRequiresSymbolResolution = requiresSymbolResolution;
                this.SerializedArgumentValueOverrides = argumentValueOverrides;
            }

            [DataMember(EmitDefaultValue = false, Name = "requiresSymbolResolution")]
            internal bool SerializedRequiresSymbolResolution { get; set; }

            [DataMember(EmitDefaultValue = false, Name = "argumentValueOverrides")]
            internal IDictionary<string, object> SerializedArgumentValueOverrides { get; set; }

            public void Initialize(ActivityInstance activityInstance, bool requiresSymbolResolution, IDictionary<string, object> argumentValueOverrides)
            {
                base.Reinitialize(activityInstance);
                this.SerializedRequiresSymbolResolution = requiresSymbolResolution;
                this.SerializedArgumentValueOverrides = argumentValueOverrides;
            }

            protected override void ReleaseToPool(ActivityExecutor executor)
            {
                base.ClearForReuse();
                this.SerializedRequiresSymbolResolution = false;
                this.SerializedArgumentValueOverrides = null;

                executor.ExecuteActivityWorkItemPool.Release(this);
            }

            public override void TraceScheduled()
            {
                if (TD.IsEnd2EndActivityTracingEnabled() && TD.ScheduleExecuteActivityWorkItemIsEnabled())
                {
                    TD.ScheduleExecuteActivityWorkItem(this.ActivityInstance.Activity.GetType().ToString(), this.ActivityInstance.Activity.DisplayName, this.ActivityInstance.Id);
                }
            }

            public override void TraceStarting()
            {
                if (TD.IsEnd2EndActivityTracingEnabled() && TD.StartExecuteActivityWorkItemIsEnabled())
                {
                    TD.StartExecuteActivityWorkItem(this.ActivityInstance.Activity.GetType().ToString(), this.ActivityInstance.Activity.DisplayName, this.ActivityInstance.Id);
                }
            }

            public override void TraceCompleted()
            {
                if (TD.IsEnd2EndActivityTracingEnabled() && TD.CompleteExecuteActivityWorkItemIsEnabled())
                {
                    TD.CompleteExecuteActivityWorkItem(this.ActivityInstance.Activity.GetType().ToString(), this.ActivityInstance.Activity.DisplayName, this.ActivityInstance.Id);
                }
            }

            public override bool Execute(ActivityExecutor executor, BookmarkManager bookmarkManager) => this.ExecuteBody(executor, bookmarkManager, null);

            protected bool ExecuteBody(ActivityExecutor executor, BookmarkManager bookmarkManager, Location resultLocation)
            {
                try
                {
                    if (this.SerializedRequiresSymbolResolution)
                    {
                        if (!this.ActivityInstance.ResolveArguments(executor, this.SerializedArgumentValueOverrides, resultLocation))
                        {
                            return true;
                        }

                        if (!this.ActivityInstance.ResolveVariables(executor))
                        {
                            return true;
                        }
                    }
                    // We want to do this if there was no symbol resolution or if ResolveVariables
                    // completed synchronously.
                    this.ActivityInstance.SetInitializedSubstate(executor);

                    if (executor.IsDebugged())
                    {
                        executor._debugController?.ActivityStarted(this.ActivityInstance);
                    }

                    this.ActivityInstance.Execute(executor, bookmarkManager);
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    this.ExceptionToPropagate = e;
                }

                return true;
            }
        }

        [DataContract]
        internal class ExecuteRootWorkItem : ExecuteActivityWorkItem
        {
            public ExecuteRootWorkItem(ActivityInstance activityInstance, bool requiresSymbolResolution, IDictionary<string, object> argumentValueOverrides)
                : base(activityInstance, requiresSymbolResolution, argumentValueOverrides)
            {
            }

            public override bool Execute(ActivityExecutor executor, BookmarkManager bookmarkManager)
            {
                if (executor.ShouldTrackActivityScheduledRecords)
                {
                    executor.AddTrackingRecord(
                        new ActivityScheduledRecord(
                            executor.WorkflowInstanceId,
                            null,
                            this.ActivityInstance));
                }

                return this.ExecuteBody(executor, bookmarkManager, null);
            }
        }

        [DataContract]
        internal class ExecuteExpressionWorkItem : ExecuteActivityWorkItem
        {
            public ExecuteExpressionWorkItem(ActivityInstance activityInstance, bool requiresSymbolResolution, IDictionary<string, object> argumentValueOverrides, Location resultLocation)
                : base(activityInstance, requiresSymbolResolution, argumentValueOverrides)
            {
                Fx.Assert(resultLocation != null, "We should only use this work item when we are resolving arguments/variables and therefore have a result location.");
                this.SerializedResultLocation = resultLocation;
            }

            [DataMember(Name = "resultLocation")]
            internal Location SerializedResultLocation { get; set; }

            public override bool Execute(ActivityExecutor executor, BookmarkManager bookmarkManager) => this.ExecuteBody(executor, bookmarkManager, this.SerializedResultLocation);
        }

        [DataContract]
        internal class PropagateExceptionWorkItem : ActivityExecutionWorkItem
        {
            public PropagateExceptionWorkItem(Exception exception, ActivityInstance activityInstance)
                : base(activityInstance)
            {
                Fx.Assert(exception != null, "We must not have a null exception.");

                this.SerializedException = exception;
                this.IsEmpty = true;
            }

            [DataMember(EmitDefaultValue = false, Name = "exception")]
            internal Exception SerializedException { get; set; }

            public override void TraceScheduled() => this.TraceRuntimeWorkItemScheduled();

            public override void TraceStarting() => this.TraceRuntimeWorkItemStarting();

            public override void TraceCompleted() => this.TraceRuntimeWorkItemCompleted();

            public override bool Execute(ActivityExecutor executor, BookmarkManager bookmarkManager)
            {
                Fx.Assert("This shouldn't be called because we are empty.");

                return false;
            }

            public override void PostProcess(ActivityExecutor executor) => this.ExceptionToPropagate = this.SerializedException;
        }

        [DataContract]
        internal class RethrowExceptionWorkItem : WorkItem
        {
            public RethrowExceptionWorkItem(ActivityInstance activityInstance, Exception exception, ActivityInstanceReference source)
                : base(activityInstance)
            {
                this.SerializedException = exception;
                this.SerializedSource = source;
                this.IsEmpty = true;
            }

            public override bool IsValid => this.ActivityInstance.State == ActivityInstanceState.Executing;

            public override ActivityInstance PropertyManagerOwner
            {
                get
                {
                    Fx.Assert("This is never called.");

                    return null;
                }
            }

            public override ActivityInstance OriginalExceptionSource => this.SerializedSource.ActivityInstance;

            [DataMember(Name = "exception")]
            internal Exception SerializedException { get; set; }

            [DataMember(Name = "source")]
            internal ActivityInstanceReference SerializedSource { get; set; }

            public override void TraceCompleted() => this.TraceRuntimeWorkItemCompleted();

            public override void TraceScheduled() => this.TraceRuntimeWorkItemScheduled();

            public override void TraceStarting() => this.TraceRuntimeWorkItemStarting();

            public override bool Execute(ActivityExecutor executor, BookmarkManager bookmarkManager)
            {
                Fx.Assert("This shouldn't be called because we are IsEmpty = true.");

                return true;
            }

            public override void PostProcess(ActivityExecutor executor)
            {
                executor.AbortActivityInstance(this.ActivityInstance, this.ExceptionToPropagate);
                this.ExceptionToPropagate = this.SerializedException;
            }
        }

        [DataContract]
        internal class TransactionContextWaiter
        {
            public TransactionContextWaiter(ActivityInstance instance, bool isRequires, RuntimeTransactionHandle handle, TransactionContextWaiterCallbackWrapper callbackWrapper, object state)
            {
                Fx.Assert(instance != null, "Must have an instance.");
                Fx.Assert(handle != null, "Must have a handle.");
                Fx.Assert(callbackWrapper != null, "Must have a callbackWrapper");

                this.WaitingInstance = instance;
                this.IsRequires = isRequires;
                this.Handle = handle;
                this.State = state;
                this.CallbackWrapper = callbackWrapper;
            }

            public ActivityInstance WaitingInstance { get; private set; }

            public bool IsRequires { get; private set; }

            public RuntimeTransactionHandle Handle { get; private set; }

            public object State { get; private set; }

            public TransactionContextWaiterCallbackWrapper CallbackWrapper { get; private set; }

            [DataMember(Name = "WaitingInstance")]
            internal ActivityInstance SerializedWaitingInstance
            {
                get { return this.WaitingInstance; }
                set { this.WaitingInstance = value; }
            }

            [DataMember(EmitDefaultValue = false, Name = "IsRequires")]
            internal bool SerializedIsRequires
            {
                get { return this.IsRequires; }
                set { this.IsRequires = value; }
            }

            [DataMember(Name = "Handle")]
            internal RuntimeTransactionHandle SerializedHandle
            {
                get { return this.Handle; }
                set { this.Handle = value; }
            }

            [DataMember(EmitDefaultValue = false, Name = "State")]
            internal object SerializedState
            {
                get { return this.State; }
                set { this.State = value; }
            }

            [DataMember(Name = "CallbackWrapper")]
            internal TransactionContextWaiterCallbackWrapper SerializedCallbackWrapper
            {
                get { return this.CallbackWrapper; }
                set { this.CallbackWrapper = value; }
            }
        }

        [DataContract]
        internal class TransactionContextWaiterCallbackWrapper : CallbackWrapper
        {
            private static readonly Type callbackType = typeof(Action<NativeActivityTransactionContext, object>);
            private static readonly Type[] transactionCallbackParameterTypes = new Type[] { typeof(NativeActivityTransactionContext), typeof(object) };

            public TransactionContextWaiterCallbackWrapper(Action<NativeActivityTransactionContext, object> action, ActivityInstance owningInstance)
                : base(action, owningInstance)
            {
            }

            [Fx.Tag.SecurityNote(Critical = "Because we are calling EnsureCallback",
                Safe = "Safe because the method needs to be part of an Activity and we are casting to the callback type and it has a very specific signature. The author of the callback is buying into being invoked from PT.")]
            [SecuritySafeCritical]
            public void Invoke(NativeActivityTransactionContext context, object value)
            {
                this.EnsureCallback(callbackType, transactionCallbackParameterTypes);
                var callback = (Action<NativeActivityTransactionContext, object>)this.Callback;
                callback(context, value);
            }
        }

        // This is not DataContract because this is always scheduled in a no-persist zone. This work
        // items exits the no persist zone when it is released.
        private partial class CompleteTransactionWorkItem : WorkItem
        {
            private static AsyncCallback persistCompleteCallback;
            private static AsyncCallback commitCompleteCallback;
            private static Action<object, TimeoutException> outcomeDeterminedCallback;
            private RuntimeTransactionData _runtimeTransaction;
            private ActivityExecutor _executor;

            public CompleteTransactionWorkItem(ActivityInstance instance)
                : base(instance)
            {
                this.ExitNoPersistRequired = true;
            }

            private static AsyncCallback PersistCompleteCallback
            {
                get
                {
                    if (persistCompleteCallback == null)
                    {
                        persistCompleteCallback = Fx.ThunkCallback(new AsyncCallback(OnPersistComplete));
                    }

                    return persistCompleteCallback;
                }
            }

            private static AsyncCallback CommitCompleteCallback
            {
                get
                {
                    if (commitCompleteCallback == null)
                    {
                        commitCompleteCallback = Fx.ThunkCallback(new AsyncCallback(OnCommitComplete));
                    }

                    return commitCompleteCallback;
                }
            }

            private static Action<object, TimeoutException> OutcomeDeterminedCallback
            {
                get
                {
                    if (outcomeDeterminedCallback == null)
                    {
                        outcomeDeterminedCallback = new Action<object, TimeoutException>(OnOutcomeDetermined);
                    }

                    return outcomeDeterminedCallback;
                }
            }

            public override bool IsValid => true;

            public override ActivityInstance PropertyManagerOwner => null;

            public override void TraceCompleted() => this.TraceRuntimeWorkItemCompleted();

            public override void TraceScheduled() => this.TraceRuntimeWorkItemScheduled();

            public override void TraceStarting() => this.TraceRuntimeWorkItemStarting();

            public override bool Execute(ActivityExecutor executor, BookmarkManager bookmarkManager)
            {
                this._runtimeTransaction = executor._runtimeTransaction;
                this._executor = executor;

                // We need to take care of any pending cancelation
                this._executor.SchedulePendingCancelation();

                bool completeSelf;
                try
                {
                    // If the transaction is already rolled back, skip the persistence. This allows
                    // us to avoid aborting the instance.
                    completeSelf = this.CheckTransactionAborted();
                    if (!completeSelf)
                    {
                        IAsyncResult result = new TransactionalPersistAsyncResult(this._executor, PersistCompleteCallback, this);
                        if (result.CompletedSynchronously)
                        {
                            completeSelf = this.FinishPersist(result);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    this.HandleException(e);
                    completeSelf = true;
                }

                if (completeSelf)
                {
                    this._executor._runtimeTransaction = null;

                    this.TraceTransactionOutcome();
                    return true;
                }

                return false;
            }

            private void TraceTransactionOutcome()
            {
                if (TD.RuntimeTransactionCompleteIsEnabled())
                {
                    TD.RuntimeTransactionComplete(this._runtimeTransaction.TransactionStatus.ToString());
                }
            }

            private void HandleException(Exception exception)
            {
                try
                {
                    this._runtimeTransaction.OriginalTransaction.Rollback(exception);
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    this._workflowAbortException = e;
                }

                if (this._runtimeTransaction.TransactionHandle.AbortInstanceOnTransactionFailure)
                {
                    // We might be overwriting a more recent exception from above, but it is more
                    // important that we tell the user why they failed originally.
                    this._workflowAbortException = exception;
                }
                else
                {
                    this.ExceptionToPropagate = exception;
                }
            }

            private static void OnPersistComplete(IAsyncResult result)
            {
                if (result.CompletedSynchronously)
                {
                    return;
                }

                var thisPtr = (CompleteTransactionWorkItem)result.AsyncState;
                var completeSelf = true;

                try
                {
                    completeSelf = thisPtr.FinishPersist(result);
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    thisPtr.HandleException(e);
                    completeSelf = true;
                }

                if (completeSelf)
                {
                    thisPtr._executor._runtimeTransaction = null;

                    thisPtr.TraceTransactionOutcome();

                    thisPtr._executor.FinishWorkItem(thisPtr);
                }
            }

            private bool FinishPersist(IAsyncResult result)
            {
                TransactionalPersistAsyncResult.End(result);

                return this.CompleteTransaction();
            }

            private bool CompleteTransaction()
            {
                PreparingEnlistment? enlistment = null;

                lock (this._runtimeTransaction)
                {
                    if (this._runtimeTransaction.PendingPreparingEnlistment != null)
                    {
                        enlistment = this._runtimeTransaction.PendingPreparingEnlistment;
                    }

                    this._runtimeTransaction.HasPrepared = true;
                }

                if (enlistment != null)
                {
                    enlistment.Prepared();
                }

                var original = this._runtimeTransaction.OriginalTransaction;

                var dependentTransaction = original as DependentTransaction;
                if (dependentTransaction != null)
                {
                    dependentTransaction.Complete();
                    return this.CheckOutcome();
                }
                else
                {
                    var committableTransaction = original as CommittableTransaction;
                    if (committableTransaction != null)
                    {
                        var result = committableTransaction.BeginCommit(CommitCompleteCallback, this);

                        return result.CompletedSynchronously ? this.FinishCommit(result) : false;
                    }
                    else
                    {
                        return this.CheckOutcome();
                    }
                }
            }

            private static void OnCommitComplete(IAsyncResult result)
            {
                if (result.CompletedSynchronously)
                {
                    return;
                }

                var thisPtr = (CompleteTransactionWorkItem)result.AsyncState;
                bool completeSelf;
                try
                {
                    completeSelf = thisPtr.FinishCommit(result);
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    thisPtr.HandleException(e);
                    completeSelf = true;
                }

                if (completeSelf)
                {
                    thisPtr._executor._runtimeTransaction = null;

                    thisPtr.TraceTransactionOutcome();

                    thisPtr._executor.FinishWorkItem(thisPtr);
                }
            }

            private bool FinishCommit(IAsyncResult result)
            {
                ((CommittableTransaction)this._runtimeTransaction.OriginalTransaction).EndCommit(result);

                return this.CheckOutcome();
            }

            private bool CheckOutcome()
            {
                AsyncWaitHandle? completionEvent = null;

                lock (this._runtimeTransaction)
                {
                    var status = this._runtimeTransaction.TransactionStatus;

                    if (status == TransactionStatus.Active)
                    {
                        completionEvent = new AsyncWaitHandle();
                        this._runtimeTransaction.CompletionEvent = completionEvent;
                    }
                }

                if (completionEvent != null)
                {
                    if (!completionEvent.WaitAsync(OutcomeDeterminedCallback, this, ActivityDefaults.TransactionCompletionTimeout))
                    {
                        return false;
                    }
                }

                return this.FinishCheckOutcome();
            }

            private static void OnOutcomeDetermined(object state, TimeoutException asyncException)
            {
                var thisPtr = (CompleteTransactionWorkItem)state;
                var completeSelf = true;

                if (asyncException != null)
                {
                    thisPtr.HandleException(asyncException);
                }
                else
                {
                    try
                    {
                        completeSelf = thisPtr.FinishCheckOutcome();
                    }
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                        {
                            throw;
                        }

                        thisPtr.HandleException(e);
                        completeSelf = true;
                    }
                }

                if (completeSelf)
                {
                    thisPtr._executor._runtimeTransaction = null;

                    thisPtr.TraceTransactionOutcome();

                    thisPtr._executor.FinishWorkItem(thisPtr);
                }
            }

            private bool FinishCheckOutcome()
            {
                this.CheckTransactionAborted();
                return true;
            }

            private bool CheckTransactionAborted()
            {
                try
                {
                    TransactionHelper.ThrowIfTransactionAbortedOrInDoubt(this._runtimeTransaction.OriginalTransaction);
                    return false;
                }
                catch (TransactionException exception)
                {
                    if (this._runtimeTransaction.TransactionHandle.AbortInstanceOnTransactionFailure)
                    {
                        this._workflowAbortException = exception;
                    }
                    else
                    {
                        this.ExceptionToPropagate = exception;
                    }
                    return true;
                }
            }

            public override void PostProcess(ActivityExecutor executor)
            {
            }
        }
    }
}
