// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Tracking;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.Runtime.Serialization;

    /// <summary>
    /// The ActivityInstance class. This class cannot be inherited. Implements the <see
    /// cref="System.Activities.Runtime.ActivityInstanceMap.IActivityReference" />
    /// </summary>
    /// <seealso cref="System.Activities.Runtime.ActivityInstanceMap.IActivityReference" />
    [DataContract(Name = XD.ActivityInstance.Name, Namespace = XD.Runtime.Namespace)]
    [Fx.Tag.XamlVisible(false)]
    public sealed partial class ActivityInstance : ActivityInstanceMap.IActivityReferenceWithEnvironment
    {
        /// <summary>
        /// The activity
        /// </summary>
        private Activity? activity;

        /// <summary>
        /// The child cache
        /// </summary>
        private ReadOnlyCollection<ActivityInstance>? childCache;

        /// <summary>
        /// The child list
        /// </summary>
        private ChildList? childList;

        /// <summary>
        /// The environment
        /// </summary>
        private LocationEnvironment? environment;

        /// <summary>
        /// The owner name
        /// </summary>
        private string? ownerName;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityInstance" /> class.
        /// </summary>
        internal ActivityInstance()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityInstance" /> class.
        /// </summary>
        /// <param name="activity">The activity.</param>
        internal ActivityInstance(Activity activity)
        {
            this.activity = activity;
            this.State = ActivityInstanceState.Executing;
            this.SerializedSubstate = Substate.Created;

            this.ImplementationVersion = activity.ImplementationVersion;
        }

        /// <summary>
        /// Gets the activity.
        /// </summary>
        /// <value>The activity.</value>
        public Activity? Activity
        {
            get => this.activity;

            internal set
            {
                Fx.Assert(value != null || this.State == ActivityInstanceState.Closed, string.Empty);
                this.activity = value;
            }
        }

        /// <summary>
        /// Gets the activity.
        /// </summary>
        /// <value>The activity.</value>
        Activity? ActivityInstanceMap.IActivityReference.Activity => this.Activity;

        /// <summary>
        /// Gets a value indicating whether this instance has activity references.
        /// </summary>
        /// <value><c>true</c> if this instance has activity references; otherwise, <c>false</c>.</value>
        internal bool HasActivityReferences => this.SerializedExtendedData != null && this.SerializedExtendedData.HasActivityReferences;

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        public string Id => this.SerializedId.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Gets the implementation version.
        /// </summary>
        /// <value>The implementation version.</value>
        [DataMember(EmitDefaultValue = false)]
        public Version ImplementationVersion { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether this instance is completed.
        /// </summary>
        /// <value><c>true</c> if this instance is completed; otherwise, <c>false</c>.</value>
        public bool IsCompleted => ActivityUtilities.IsCompletedState(this.State);

        /// <summary>
        /// Gets the state.
        /// </summary>
        /// <value>The state.</value>
        public ActivityInstanceState State { get; private set; }

        /// <summary>
        /// Gets or sets the compiled data contexts.
        /// </summary>
        /// <value>The compiled data contexts.</value>
        internal object? CompiledDataContexts { get; set; }

        /// <summary>
        /// Gets or sets the compiled data contexts for implementation.
        /// </summary>
        /// <value>The compiled data contexts for implementation.</value>
        internal object CompiledDataContextsForImplementation { get; set; }

        /// <summary>
        /// Gets or sets the completion bookmark.
        /// </summary>
        /// <value>The completion bookmark.</value>
        [DataMember(EmitDefaultValue = false)]
        internal CompletionBookmark CompletionBookmark { get; set; }

        /// <summary>
        /// Gets or sets the data context.
        /// </summary>
        /// <value>The data context.</value>
        internal WorkflowDataContext? DataContext
        {
            get => this.SerializedExtendedData?.DataContext;
            set
            {
                this.EnsureExtendedData();
                this.SerializedExtendedData.DataContext = value;
            }
        }

        /// <summary>
        /// Gets the environment.
        /// </summary>
        /// <value>The environment.</value>
        internal LocationEnvironment Environment
        {
            get
            {
                Fx.Assert(this.environment != null, "There should always be an environment");
                return this.environment;
            }
        }

        /// <summary>
        /// Gets or sets the fault bookmark.
        /// </summary>
        /// <value>The fault bookmark.</value>
        internal FaultBookmark? FaultBookmark
        {
            get => this.SerializedExtendedData?.FaultBookmark;

            set
            {
                Fx.Assert(
                    value != null || (this.SerializedExtendedData == null || this.SerializedExtendedData.FaultBookmark == null),
                    "cannot go from non-null to null");
                if (value != null)
                {
                    this.EnsureExtendedData();
                    this.SerializedExtendedData.FaultBookmark = value;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has children.
        /// </summary>
        /// <value><c>true</c> if this instance has children; otherwise, <c>false</c>.</value>
        internal bool HasChildren => (this.childList != null && this.childList.Count > 0);

        /// <summary>
        /// Gets a value indicating whether this instance has not executed.
        /// </summary>
        /// <value><c>true</c> if this instance has not executed; otherwise, <c>false</c>.</value>
        internal bool HasNotExecuted => (this.SerializedSubstate & Substate.PreExecuting) != 0;

        /// <summary>
        /// Gets a value indicating whether this instance has pending work.
        /// </summary>
        /// <value><c>true</c> if this instance has pending work; otherwise, <c>false</c>.</value>
        internal bool HasPendingWork =>
                // check if we have pending bookmarks or outstanding OperationControlContexts/WorkItems
                this.HasChildren || this.SerializedBusyCount > 0;

        /// <summary>
        /// Gets the instance map.
        /// </summary>
        /// <value>The instance map.</value>
        internal ActivityInstanceMap InstanceMap { get; private set; }

        /// <summary>
        /// Gets the internal identifier.
        /// </summary>
        /// <value>The internal identifier.</value>
        internal long InternalId => this.SerializedId;

        /// <summary>
        /// Gets or sets a value indicating whether this instance is cancellation requested.
        /// </summary>
        /// <value><c>true</c> if this instance is cancellation requested; otherwise, <c>false</c>.</value>
        internal bool IsCancellationRequested
        {
            get => this.SerializedIsCancellationRequested;
            set
            {
                // This is set at the time of scheduling the cancelation work item

                Fx.Assert(!this.SerializedIsCancellationRequested, "We should not set this if we have already requested cancel.");
                Fx.Assert(value != false, "We should only set this to true.");

                this.SerializedIsCancellationRequested = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is environment owner.
        /// </summary>
        /// <value><c>true</c> if this instance is environment owner; otherwise, <c>false</c>.</value>
        internal bool IsEnvironmentOwner => !this.SerializedNoSymbols;

        /// <summary>
        /// Gets a value indicating whether this instance is performing default cancelation.
        /// </summary>
        /// <value><c>true</c> if this instance is performing default cancelation; otherwise, <c>false</c>.</value>
        internal bool IsPerformingDefaultCancelation => this.SerializedPerformingDefaultCancelation;

        /// <summary>
        /// Gets a value indicating whether this instance is resolving arguments.
        /// </summary>
        /// <value><c>true</c> if this instance is resolving arguments; otherwise, <c>false</c>.</value>
        internal bool IsResolvingArguments => this.SerializedSubstate == Substate.ResolvingArguments;

        /// <summary>
        /// Gets a value indicating whether [only has outstanding bookmarks].
        /// </summary>
        /// <value><c>true</c> if [only has outstanding bookmarks]; otherwise, <c>false</c>.</value>
        internal bool OnlyHasOutstandingBookmarks =>
                // If our whole busy count is because of blocking bookmarks then we should return true
                !this.HasChildren && this.SerializedExtendedData != null && (this.SerializedExtendedData.BlockingBookmarkCount == this.SerializedBusyCount);

        /// <summary>
        /// Gets or sets the name of the owner.
        /// </summary>
        /// <value>The name of the owner.</value>
        [DataMember(Name = XD.ActivityInstance.Owner, EmitDefaultValue = false)]
        internal string OwnerName
        {
            get
            {
                if (this.ownerName == null)
                {
                    this.ownerName = this.Activity?.GetType().Name;
                }

                return this.ownerName;
            }
            set
            {
                Fx.Assert(value != null, "value from Serialization should not be null");
                this.ownerName = value;
            }
        }

        /// <summary>
        /// Gets the parent.
        /// </summary>
        /// <value>The parent.</value>
        internal ActivityInstance Parent { get; private set; }

        /// <summary>
        /// Gets or sets the property manager.
        /// </summary>
        /// <value>The property manager.</value>
        internal ExecutionPropertyManager PropertyManager { get; set; }

        /// <summary>
        /// Gets or sets the serialized busy count.
        /// </summary>
        /// <value>The serialized busy count.</value>
        [DataMember(EmitDefaultValue = false, Name = "busyCount")]
        internal int SerializedBusyCount { get; set; }

        /// <summary>
        /// Gets or sets the serialized children.
        /// </summary>
        /// <value>The serialized children.</value>
        [DataMember(Name = XD.ActivityInstance.Children, EmitDefaultValue = false)]
        internal ChildList? SerializedChildren
        {
            get
            {
                if (this.HasChildren)
                {
                    this.childList?.Compress();
                    return this.childList;
                }

                return null;
            }

            set
            {
                Fx.Assert(value != null, "value from Serialization should not be null");
                this.childList = value;
            }
        }

        /// <summary>
        /// Gets or sets the serialized environment.
        /// </summary>
        /// <value>The serialized environment.</value>
        [DataMember(EmitDefaultValue = false)]
        internal LocationEnvironment? SerializedEnvironment
        {
            get
            {
                return this.IsCompleted ? null : this.environment;
            }
            set
            {
                Fx.Assert(value != null, "We should never get null here.");

                this.environment = value;
            }
        }

        /// <summary>
        /// Gets or sets the serialized extended data.
        /// </summary>
        /// <value>The serialized extended data.</value>
        [DataMember(EmitDefaultValue = false, Name = "extendedData")]
        internal ExtendedData SerializedExtendedData { get; set; }

        /// <summary>
        /// Gets or sets the serialized identifier.
        /// </summary>
        /// <value>The serialized identifier.</value>
        [DataMember(EmitDefaultValue = false, Name = "id")]
        internal long SerializedId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [serialized initialization incomplete].
        /// </summary>
        /// <value><c>true</c> if [serialized initialization incomplete]; otherwise, <c>false</c>.</value>
        [DataMember(EmitDefaultValue = false, Name = "initializationIncomplete")]
        internal bool SerializedInitializationIncomplete { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [serialized is cancellation requested].
        /// </summary>
        /// <value><c>true</c> if [serialized is cancellation requested]; otherwise, <c>false</c>.</value>
        [DataMember(EmitDefaultValue = false, Name = "isCancellationRequested")]
        internal bool SerializedIsCancellationRequested { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [serialized no symbols].
        /// </summary>
        /// <value><c>true</c> if [serialized no symbols]; otherwise, <c>false</c>.</value>
        [DataMember(EmitDefaultValue = false, Name = "noSymbols")]
        internal bool SerializedNoSymbols { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [serialized performing default cancelation].
        /// </summary>
        /// <value><c>true</c> if [serialized performing default cancelation]; otherwise, <c>false</c>.</value>
        [DataMember(EmitDefaultValue = false, Name = "performingDefaultCancelation")]
        internal bool SerializedPerformingDefaultCancelation { get; set; }

        /// <summary>
        /// Gets or sets the serialized property manager.
        /// </summary>
        /// <value>The serialized property manager.</value>
        [DataMember(Name = XD.ActivityInstance.PropertyManager, EmitDefaultValue = false)]
        //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode, Justification = "Called from Serialization")]
        internal ExecutionPropertyManager? SerializedPropertyManager
        {
            get => this.PropertyManager == null || !this.PropertyManager.ShouldSerialize(this) ? null : this.PropertyManager;
            set
            {
                Fx.Assert(value != null, "We don't emit the default value so this should never be null.");
                this.PropertyManager = value;
            }
        }

        /// <summary>
        /// Gets or sets the state of the serialized.
        /// </summary>
        /// <value>The state of the serialized.</value>
        [DataMember(EmitDefaultValue = false, Name = "state")]
        internal ActivityInstanceState SerializedState
        {
            get => this.State;
            set => this.State = value;
        }

        /// <summary>
        /// Gets or sets the serialized substate.
        /// </summary>
        /// <value>The serialized substate.</value>
        [DataMember(EmitDefaultValue = false, Name = "substate")]
        internal Substate SerializedSubstate { get; set; }

        /// <summary>
        /// Gets the state of the sub.
        /// </summary>
        /// <value>The state of the sub.</value>
        internal Substate SubState => this.SerializedSubstate;

        /// <summary>
        /// Gets or sets a value indicating whether [waiting for transaction context].
        /// </summary>
        /// <value><c>true</c> if [waiting for transaction context]; otherwise, <c>false</c>.</value>
        internal bool WaitingForTransactionContext
        {
            get => this.SerializedExtendedData == null ? false : this.SerializedExtendedData.WaitingForTransactionContext;
            set
            {
                this.EnsureExtendedData();

                this.SerializedExtendedData.WaitingForTransactionContext = value;
            }
        }

        /// <summary>
        /// Loads the specified activity.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <param name="instanceMap">The instance map.</param>
        /// <exception cref="ValidationException"></exception>
        /// <exception cref="VersionMismatchException"></exception>
        void ActivityInstanceMap.IActivityReference.Load(Activity activity, ActivityInstanceMap instanceMap)
        {
            if (activity.GetType().Name != this.OwnerName)
            {
                throw FxTrace.Exception.AsError(
                    new ValidationException(SR.ActivityTypeMismatch(activity.DisplayName, this.OwnerName)));
            }

            if (activity.ImplementationVersion != this.ImplementationVersion)
            {
                throw FxTrace.Exception.AsError(new VersionMismatchException(SR.ImplementationVersionMismatch(this.ImplementationVersion, activity.ImplementationVersion, activity)));
            }

            this.Activity = activity;
        }

        /// <summary>
        /// Creates the canceled instance.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <returns>ActivityInstance.</returns>
        internal static ActivityInstance CreateCanceledInstance(Activity activity)
        {
            var instance = new ActivityInstance(activity)
            {
                State = ActivityInstanceState.Canceled
            };

            return instance;
        }

        /// <summary>
        /// Creates the completed instance.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <returns>ActivityInstance.</returns>
        internal static ActivityInstance CreateCompletedInstance(Activity activity)
        {
            var instance = new ActivityInstance(activity)
            {
                State = ActivityInstanceState.Closed
            };

            return instance;
        }

        /// <summary>
        /// Aborts the specified executor.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="bookmarkManager">The bookmark manager.</param>
        /// <param name="terminationReason">The termination reason.</param>
        /// <param name="isTerminate">if set to <c>true</c> [is terminate].</param>
        internal void Abort(ActivityExecutor executor, BookmarkManager bookmarkManager, Exception terminationReason, bool isTerminate)
        {
            // This is a gentle abort where we try to keep the runtime in a usable state.
            using var abortEnumerator = new AbortEnumerator(this);
            while (abortEnumerator.MoveNext())
            {
                var currentInstance = abortEnumerator.Current;

                if (!currentInstance.HasNotExecuted)
                {
                    currentInstance.Activity.InternalAbort(currentInstance, executor, terminationReason);

                    executor.DebugActivityCompleted(currentInstance);
                }

                if (currentInstance.PropertyManager != null)
                {
                    currentInstance.PropertyManager.UnregisterProperties(currentInstance, currentInstance.Activity.MemberOf, true);
                }

                executor.TerminateSpecialExecutionBlocks(currentInstance, terminationReason);

                executor.CancelPendingOperation(currentInstance);

                executor.HandleRootCompletion(currentInstance);

                currentInstance.MarkAsComplete(executor.RawBookmarkScopeManager, bookmarkManager);

                currentInstance.State = ActivityInstanceState.Faulted;

                currentInstance.FinalizeState(executor, false, !isTerminate);
            }
        }

        /// <summary>
        /// Adds the activity reference.
        /// </summary>
        /// <param name="reference">The reference.</param>
        internal void AddActivityReference(ActivityInstanceReference reference)
        {
            this.EnsureExtendedData();
            this.SerializedExtendedData.AddActivityReference(reference);
        }

        /// <summary>
        /// Adds the bookmark.
        /// </summary>
        /// <param name="bookmark">The bookmark.</param>
        /// <param name="options">The options.</param>
        internal void AddBookmark(Bookmark bookmark, BookmarkOptions options)
        {
            var affectsBusyCount = false;

            if (!BookmarkOptionsHelper.IsNonBlocking(options))
            {
                this.IncrementBusyCount();
                affectsBusyCount = true;
            }

            this.EnsureExtendedData();
            this.SerializedExtendedData.AddBookmark(bookmark, affectsBusyCount);
        }

        /// <summary>
        /// Adds the child.
        /// </summary>
        /// <param name="item">The item.</param>
        internal void AddChild(ActivityInstance item)
        {
            if (this.childList == null)
            {
                this.childList = new ChildList();
            }

            this.childList.Add(item);
            this.childCache = null;
        }

        /// <summary>
        /// Appends the children.
        /// </summary>
        /// <param name="nextInstanceList">The next instance list.</param>
        /// <param name="instancesRemaining">The instances remaining.</param>
        /// <remarks>called by ActivityUtilities tree-walk</remarks>
        internal void AppendChildren(ActivityUtilities.TreeProcessingList nextInstanceList, ref Queue<IList<ActivityInstance>> instancesRemaining)
        {
            Fx.Assert(this.HasChildren, "AppendChildren is tuned to only be called when HasChildren is true");
            this.childList.AppendChildren(nextInstanceList, ref instancesRemaining);
        }

        /// <summary>
        /// Bases the cancel.
        /// </summary>
        /// <param name="context">The context.</param>
        internal void BaseCancel(NativeActivityContext context)
        {
            // Default cancelation logic starts here, but is also performed in UpdateState and
            // through special completion work items

            Fx.Assert(this.IsCancellationRequested, "This should be marked to true at this point.");

            this.SerializedPerformingDefaultCancelation = true;

            this.CancelChildren(context);
        }

        /// <summary>
        /// Cancels the specified executor.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="bookmarkManager">The bookmark manager.</param>
        internal void Cancel(ActivityExecutor executor, BookmarkManager bookmarkManager) =>
            this.Activity.InternalCancel(this, executor, bookmarkManager);

        /// <summary>
        /// Cancels the children.
        /// </summary>
        /// <param name="context">The context.</param>
        internal void CancelChildren(NativeActivityContext context)
        {
            if (this.HasChildren)
            {
                foreach (var child in this.GetChildren())
                {
                    context.CancelChild(child);
                }
            }
        }

        /// <summary>
        /// Decrements the busy count.
        /// </summary>
        internal void DecrementBusyCount()
        {
            Fx.Assert(this.SerializedBusyCount > 0, "something went wrong with our bookkeeping");
            this.SerializedBusyCount--;
        }

        /// <summary>
        /// Decrements the busy count.
        /// </summary>
        /// <param name="amount">The amount.</param>
        internal void DecrementBusyCount(int amount)
        {
            Fx.Assert(this.SerializedBusyCount >= amount, "something went wrong with our bookkeeping");
            this.SerializedBusyCount -= amount;
        }

        /// <summary>
        /// Executes the specified executor.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="bookmarkManager">The bookmark manager.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal void Execute(ActivityExecutor executor, BookmarkManager bookmarkManager)
        {
            if (this.SerializedInitializationIncomplete)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.InitializationIncomplete));
            }

            this.MarkExecuted();
            this.Activity.InternalExecute(this, executor, bookmarkManager);
        }

        /// <summary>
        /// Fills the instance map.
        /// </summary>
        /// <param name="instanceMap">The instance map.</param>
        internal void FillInstanceMap(ActivityInstanceMap instanceMap)
        {
            if (this.IsCompleted)
            {
                // We don't bother adding completed roots to the map
                return;
            }

            Fx.Assert(this.InstanceMap == null, "We should never call this unless the current map is null.");
            Fx.Assert(this.Parent == null, "Can only generate a map from a root instance.");

            this.InstanceMap = instanceMap;
            ActivityUtilities.ProcessActivityInstanceTree(this, null, new Func<ActivityInstance, ActivityExecutor, bool>(this.GenerateInstanceMapCallback));
        }

        /// <summary>
        /// Finalizes the state.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="faultActivity">if set to <c>true</c> [fault activity].</param>
        internal void FinalizeState(ActivityExecutor executor, bool faultActivity) => this.FinalizeState(executor, faultActivity, false);

        /// <summary>
        /// Finalizes the state.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="faultActivity">if set to <c>true</c> [fault activity].</param>
        /// <param name="skipTracking">if set to <c>true</c> [skip tracking].</param>
        internal void FinalizeState(ActivityExecutor executor, bool faultActivity, bool skipTracking)
        {
            if (faultActivity)
            {
                this.TryCancelParent();

                // We can override previous completion states with this
                this.State = ActivityInstanceState.Faulted;
            }

            Fx.Assert(this.State != ActivityInstanceState.Executing, "We must be in a completed state at this point.");

            if (this.State == ActivityInstanceState.Closed)
            {
                if (executor.ShouldTrackActivityStateRecordsClosedState && !skipTracking)
                {
                    if (executor.ShouldTrackActivity(this.Activity.DisplayName))
                    {
                        executor.AddTrackingRecord(new ActivityStateRecord(executor.WorkflowInstanceId, this, this.State));
                    }
                }
            }
            else
            {
                if (executor.ShouldTrackActivityStateRecords && !skipTracking)
                {
                    executor.AddTrackingRecord(new ActivityStateRecord(executor.WorkflowInstanceId, this, this.State));
                }
            }

            if (TD.ActivityCompletedIsEnabled())
            {
                TD.ActivityCompleted(this.Activity.GetType().ToString(), this.Activity.DisplayName, this.Id, this.State.GetStateName());
            }
        }

        /// <summary>
        /// Fixups the instance.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="instanceMap">The instance map.</param>
        /// <param name="executor">The executor.</param>
        /// <exception cref="InvalidOperationException"></exception>
        /// <remarks>called after deserialization of the workflow instance</remarks>
        internal void FixupInstance(ActivityInstance parent, ActivityInstanceMap instanceMap, ActivityExecutor executor)
        {
            if (this.IsCompleted)
            {
                // We hang onto the root instance even after is it complete. We skip the fixups for
                // a completed root.
                Fx.Assert(parent == null, "This should only happen to root instances.");

                return;
            }

            if (this.Activity == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.ActivityInstanceFixupFailed));
            }

            this.Parent = parent;
            this.InstanceMap = instanceMap;

            if (this.PropertyManager != null)
            {
                this.PropertyManager.OnDeserialized(this, parent, this.Activity.MemberOf, executor);
            }
            else if (this.Parent != null)
            {
                // The current property manager is null here
                this.PropertyManager = this.Parent.PropertyManager;
            }
            else
            {
                this.PropertyManager = executor.RootPropertyManager;
            }

            if (!this.SerializedNoSymbols)
            {
                this.environment.OnDeserialized(executor, this);
            }
        }

        /// <summary>
        /// Gets the children.
        /// </summary>
        /// <returns>ReadOnlyCollection&lt;ActivityInstance&gt;.</returns>
        internal ReadOnlyCollection<ActivityInstance> GetChildren()
        {
            if (!this.HasChildren)
            {
                return ChildList.Empty;
            }

            if (this.childCache == null)
            {
                this.childCache = this.childList.AsReadOnly();
            }

            return this.childCache;
        }

        /// <summary>
        /// Gets the raw children.
        /// </summary>
        /// <returns>HybridCollection&lt;ActivityInstance&gt;.</returns>
        internal HybridCollection<ActivityInstance> GetRawChildren() => this.childList;

        // Busy Count includes the following:
        // 1. Active OperationControlContexts.
        // 2. Active work items.
        // 3. Blocking bookmarks.
        /// <summary>
        /// Increments the busy count.
        /// </summary>
        internal void IncrementBusyCount() => this.SerializedBusyCount++;

        /// <summary>
        /// Initializes the specified parent.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="instanceMap">The instance map.</param>
        /// <param name="parentEnvironment">The parent environment.</param>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="executor">The executor.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        internal bool Initialize(
            ActivityInstance parent,
            ActivityInstanceMap instanceMap,
            LocationEnvironment parentEnvironment,
            long instanceId,
            ActivityExecutor executor) =>
            this.Initialize(parent, instanceMap, parentEnvironment, instanceId, executor, 0);

        /// <summary>
        /// Initializes the specified parent.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="instanceMap">The instance map.</param>
        /// <param name="parentEnvironment">The parent environment.</param>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="executor">The executor.</param>
        /// <param name="delegateParameterCount">The delegate parameter count.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        internal bool Initialize(
            ActivityInstance parent,
            ActivityInstanceMap instanceMap,
            LocationEnvironment parentEnvironment,
            long instanceId,
            ActivityExecutor executor,
            int delegateParameterCount)
        {
            this.Parent = parent;
            this.InstanceMap = instanceMap;
            this.SerializedId = instanceId;

            if (this.InstanceMap != null)
            {
                this.InstanceMap.AddEntry(this);
            }

            // propagate necessary information from our parent
            if (this.Parent != null)
            {
                if (this.Parent.PropertyManager != null)
                {
                    this.PropertyManager = this.Parent.PropertyManager;
                }

                if (parentEnvironment == null)
                {
                    parentEnvironment = this.Parent.Environment;
                }
            }

            var symbolCount = this.Activity.SymbolCount + delegateParameterCount;

            if (symbolCount == 0)
            {
                if (parentEnvironment == null)
                {
                    // We create an environment for a root activity that otherwise would not have
                    // one to simplify environment management.
                    this.environment = new LocationEnvironment(executor, this.Activity);
                }
                else
                {
                    this.SerializedNoSymbols = true;
                    this.environment = parentEnvironment;
                }

                // We don't set Initialized here since the tracking/tracing would be too early
                return false;
            }
            else
            {
                this.environment = new LocationEnvironment(executor, this.Activity, parentEnvironment, symbolCount);
                this.SerializedSubstate = Substate.ResolvingArguments;
                return true;
            }
        }

        /// <summary>
        /// Marks as complete.
        /// </summary>
        /// <param name="bookmarkScopeManager">The bookmark scope manager.</param>
        /// <param name="bookmarkManager">The bookmark manager.</param>
        internal void MarkAsComplete(BookmarkScopeManager bookmarkScopeManager, BookmarkManager bookmarkManager)
        {
            if (this.SerializedExtendedData != null)
            {
                this.SerializedExtendedData.PurgeBookmarks(bookmarkScopeManager, bookmarkManager, this);

                if (this.SerializedExtendedData.DataContext != null)
                {
                    this.SerializedExtendedData.DataContext.Dispose();
                }
            }

            if (this.InstanceMap != null)
            {
                this.InstanceMap.RemoveEntry(this);

                if (this.HasActivityReferences)
                {
                    this.SerializedExtendedData.PurgeActivityReferences(this.InstanceMap);
                }
            }

            if (this.Parent != null)
            {
                this.Parent.RemoveChild(this);
            }
        }

        /// <summary>
        /// Marks the canceled.
        /// </summary>
        internal void MarkCanceled()
        {
            Fx.Assert(this.SerializedSubstate == Substate.Executing || this.SerializedSubstate == Substate.Canceling, "called from an unexpected state");
            this.SerializedSubstate = Substate.Canceling;
        }

        /// <summary>
        /// Removes all bookmarks.
        /// </summary>
        /// <param name="bookmarkScopeManager">The bookmark scope manager.</param>
        /// <param name="bookmarkManager">The bookmark manager.</param>
        internal void RemoveAllBookmarks(BookmarkScopeManager bookmarkScopeManager, BookmarkManager bookmarkManager)
        {
            if (this.SerializedExtendedData != null)
            {
                this.SerializedExtendedData.PurgeBookmarks(bookmarkScopeManager, bookmarkManager, this);
            }
        }

        /// <summary>
        /// Removes the bookmark.
        /// </summary>
        /// <param name="bookmark">The bookmark.</param>
        /// <param name="options">The options.</param>
        internal void RemoveBookmark(Bookmark bookmark, BookmarkOptions options)
        {
            var affectsBusyCount = false;

            if (!BookmarkOptionsHelper.IsNonBlocking(options))
            {
                this.DecrementBusyCount();
                affectsBusyCount = true;
            }

            Fx.Assert(this.SerializedExtendedData != null, "something went wrong with our bookkeeping");
            this.SerializedExtendedData.RemoveBookmark(bookmark, affectsBusyCount);
        }

        /// <summary>
        /// Removes the child.
        /// </summary>
        /// <param name="item">The item.</param>
        internal void RemoveChild(ActivityInstance item)
        {
            Fx.Assert(this.childList != null, "");
            this.childList.Remove(item, true);
            this.childCache = null;
        }

        /// <summary>
        /// Resolves the arguments.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="argumentValueOverrides">The argument value overrides.</param>
        /// <param name="resultLocation">The result location.</param>
        /// <param name="startIndex">The start index.</param>
        /// <returns><c>true</c> if arguments were resolved synchronously, <c>false</c> otherwise.</returns>
        internal bool ResolveArguments(ActivityExecutor executor, IDictionary<string, object> argumentValueOverrides, Location resultLocation, int startIndex = 0)
        {
            Fx.Assert(!this.SerializedNoSymbols, "Can only resolve arguments if we created an environment");
            Fx.Assert(this.SerializedSubstate == Substate.ResolvingArguments, "Invalid sub-state machine");

            var completedSynchronously = true;

            if (this.Activity.IsFastPath)
            {
                // We still need to resolve the result argument
                Fx.Assert(argumentValueOverrides == null, "We shouldn't have any overrides.");
                Fx.Assert(((ActivityWithResult)this.Activity).ResultRuntimeArgument != null, "We should have a result argument");

                var argument = ((ActivityWithResult)this.Activity).ResultRuntimeArgument;

                if (!argument.TryPopulateValue(this.environment, this, executor, null, resultLocation, false))
                {
                    completedSynchronously = false;

                    var location = this.environment.GetSpecificLocation(argument.Id);
                    executor.ScheduleExpression(argument.BoundArgument.Expression, this, this.Environment, location, null);
                }
            }
            else if (!this.Activity.SkipArgumentResolution)
            {
                var runtimeArguments = this.Activity.RuntimeArguments;

                var argumentCount = runtimeArguments.Count;

                if (argumentCount > 0)
                {
                    for (var i = startIndex; i < argumentCount; i++)
                    {
                        var argument = runtimeArguments[i];

                        if (!this.InternalTryPopulateArgumentValueOrScheduleExpression(argument, i, executor, argumentValueOverrides, resultLocation, false))
                        {
                            completedSynchronously = false;
                            break;
                        }
                    }
                }
            }

            if (completedSynchronously && startIndex == 0)
            {
                // We only move our state machine forward if this is the first call to
                // ResolveArguments (startIndex
                // == 0). Otherwise, a call to UpdateState will cause the substate switch (as well
                // as a call to CollapseTemporaryResolutionLocations).
                this.SerializedSubstate = Substate.ResolvingVariables;
            }

            return completedSynchronously;
        }

        /// <summary>
        /// Resolves the new arguments during dynamic update.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="dynamicUpdateArgumentIndexes">The dynamic update argument indexes.</param>
        internal void ResolveNewArgumentsDuringDynamicUpdate(ActivityExecutor executor, IList<int> dynamicUpdateArgumentIndexes)
        {
            Fx.Assert(!this.SerializedNoSymbols, "Can only resolve arguments if we created an environment");
            Fx.Assert(this.SerializedSubstate == Substate.Executing, "Dynamically added arguments are to be resolved only in Substate.Executing.");

            if (this.Activity.SkipArgumentResolution)
            {
                return;
            }

            var runtimeArguments = this.Activity.RuntimeArguments;

            for (var i = 0; i < dynamicUpdateArgumentIndexes.Count; i++)
            {
                var argument = runtimeArguments[dynamicUpdateArgumentIndexes[i]];
                Fx.Assert(this.Environment.GetSpecificLocation(argument.Id) == null, "This is a newly added argument so the location should be null");

                this.InternalTryPopulateArgumentValueOrScheduleExpression(argument, -1, executor, null, null, true);
            }
        }

        /// <summary>
        /// Resolves the new variable defaults during dynamic update.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="dynamicUpdateVariableIndexes">The dynamic update variable indexes.</param>
        /// <param name="forImplementation">if set to <c>true</c> [for implementation].</param>
        internal void ResolveNewVariableDefaultsDuringDynamicUpdate(ActivityExecutor executor, IList<int> dynamicUpdateVariableIndexes, bool forImplementation)
        {
            Fx.Assert(!this.SerializedNoSymbols, "Can only resolve variable default if we created an environment");
            Fx.Assert(this.SerializedSubstate == Substate.Executing, "Dynamically added variable default expressions are to be resolved only in Substate.Executing.");

            var runtimeVariables = forImplementation ? this.Activity.ImplementationVariables : this.Activity.RuntimeVariables;
            for (var i = 0; i < dynamicUpdateVariableIndexes.Count; i++)
            {
                var newVariable = runtimeVariables[dynamicUpdateVariableIndexes[i]];
                if (newVariable.Default != null)
                {
                    this.EnqueueVariableDefault(executor, newVariable, null);
                }
            }
        }

        /// <summary>
        /// Resolves the variables.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        internal bool ResolveVariables(ActivityExecutor executor)
        {
            Fx.Assert(!this.SerializedNoSymbols, "can only resolve variables if we created an environment");
            Fx.Assert(this.SerializedSubstate == Substate.ResolvingVariables, "invalid sub-state machine");

            this.SerializedSubstate = Substate.ResolvingVariables;
            var completedSynchronously = true;

            var implementationVariables = this.Activity.ImplementationVariables;
            var runtimeVariables = this.Activity.RuntimeVariables;

            var implementationVariableCount = implementationVariables.Count;
            var runtimeVariableCount = runtimeVariables.Count;

            if (implementationVariableCount > 0 || runtimeVariableCount > 0)
            {
                for (var i = 0; i < implementationVariableCount; i++)
                {
                    implementationVariables[i].DeclareLocation(executor, this);
                }

                for (var i = 0; i < runtimeVariableCount; i++)
                {
                    runtimeVariables[i].DeclareLocation(executor, this);
                }

                for (var i = 0; i < implementationVariableCount; i++)
                {
                    completedSynchronously &= this.ResolveVariable(implementationVariables[i], executor);
                }

                for (var i = 0; i < runtimeVariableCount; i++)
                {
                    completedSynchronously &= this.ResolveVariable(runtimeVariables[i], executor);
                }
            }

            return completedSynchronously;
        }

        /// <summary>
        /// Sets the initialization incomplete.
        /// </summary>
        internal void SetInitializationIncomplete() => this.SerializedInitializationIncomplete = true;

        /// <summary>
        /// Sets the initialized substate.
        /// </summary>
        /// <param name="executor">The executor.</param>
        internal void SetInitializedSubstate(ActivityExecutor executor)
        {
            Fx.Assert(this.SerializedSubstate != Substate.Initialized, "SetInitializedSubstate called when substate is already Initialized.");
            this.SerializedSubstate = Substate.Initialized;
            if (executor.ShouldTrackActivityStateRecordsExecutingState)
            {
                if (executor.ShouldTrackActivity(this.Activity.DisplayName))
                {
                    executor.AddTrackingRecord(new ActivityStateRecord(executor.WorkflowInstanceId, this, this.State));
                }
            }

            if (TD.InArgumentBoundIsEnabled())
            {
                var runtimeArgumentsCount = this.Activity.RuntimeArguments.Count;
                if (runtimeArgumentsCount > 0)
                {
                    for (var i = 0; i < runtimeArgumentsCount; i++)
                    {
                        var argument = this.Activity.RuntimeArguments[i];

                        if (ArgumentDirectionHelper.IsIn(argument.Direction))
                        {
                            if (this.environment.TryGetLocation(argument.Id, this.Activity, out var location))
                            {
                                var argumentValue = location.Value == null ? "<Null>" : $"'{location.Value.ToString()}'";
                                TD.InArgumentBound(argument.Name, this.Activity.GetType().ToString(), this.Activity.DisplayName, this.Id, argumentValue);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Tries the fixup children.
        /// </summary>
        /// <param name="instanceMap">The instance map.</param>
        /// <param name="executor">The executor.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        internal bool TryFixupChildren(ActivityInstanceMap instanceMap, ActivityExecutor executor)
        {
            if (!this.HasChildren)
            {
                return false;
            }

            this.childList.FixupList(this, instanceMap, executor);
            return true;
        }

        /// <summary>
        /// Updates the state.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <returns><c>true</c> if the activity completed, <c>false</c> otherwise.</returns>
        internal bool UpdateState(ActivityExecutor executor)
        {
            var activityCompleted = false;

            if (this.HasNotExecuted)
            {
                if (this.IsCancellationRequested) // need to cancel any in-flight resolutions and bail
                {
                    if (this.HasChildren)
                    {
                        foreach (var child in this.GetChildren())
                        {
                            Fx.Assert(child.State == ActivityInstanceState.Executing, "should only have children if they're still executing");
                            executor.CancelActivity(child);
                        }
                    }
                    else
                    {
                        this.SetCanceled();
                        activityCompleted = true;
                    }
                }
                else if (!this.HasPendingWork)
                {
                    var scheduleBody = false;

                    if (this.SerializedSubstate == Substate.ResolvingArguments)
                    {
                        // if we've had asynchronous resolution of Locations (Out/InOut Arguments),
                        // resolve them now
                        this.Environment.CollapseTemporaryResolutionLocations();

                        this.SerializedSubstate = Substate.ResolvingVariables;
                        scheduleBody = this.ResolveVariables(executor);
                    }
                    else if (this.SerializedSubstate == Substate.ResolvingVariables)
                    {
                        scheduleBody = true;
                    }

                    if (scheduleBody)
                    {
                        executor.ScheduleBody(this, false, null, null);
                    }
                }

                Fx.Assert(this.HasPendingWork || activityCompleted, "should have scheduled work pending if we're not complete");
            }
            else if (!this.HasPendingWork)
            {
                if (!executor.IsCompletingTransaction(this))
                {
                    activityCompleted = true;
                    if (this.SerializedSubstate == Substate.Canceling)
                    {
                        this.SetCanceled();
                    }
                    else
                    {
                        this.SetClosed();
                    }
                }
            }
            else if (this.SerializedPerformingDefaultCancelation)
            {
                if (this.OnlyHasOutstandingBookmarks)
                {
                    this.RemoveAllBookmarks(executor.RawBookmarkScopeManager, executor.RawBookmarkManager);
                    this.MarkCanceled();

                    Fx.Assert(!this.HasPendingWork, "Shouldn't have pending work here.");

                    this.SetCanceled();
                    activityCompleted = true;
                }
            }

            return activityCompleted;
        }

        /// <summary>
        /// Updates the location environment hierarchy.
        /// </summary>
        /// <param name="oldParentEnvironment">The old parent environment.</param>
        /// <param name="newEnvironment">The new environment.</param>
        /// <param name="currentInstance">The current instance.</param>
        private static void UpdateLocationEnvironmentHierarchy(
            LocationEnvironment? oldParentEnvironment,
            LocationEnvironment newEnvironment,
            ActivityInstance currentInstance)
        {
            bool processInstanceCallback(ActivityInstance instance, ActivityExecutor executor)
            {
                if (instance == currentInstance)
                {
                    return true;
                }

                if (instance.IsEnvironmentOwner)
                {
                    if (instance.environment != null && instance.environment.Parent == oldParentEnvironment)
                    {
                        // overwrite its parent with newEnvironment
                        instance.environment.Parent = newEnvironment;
                    }

                    // We do not need to process children instances beyond this point.
                    return false;
                }

                if (instance.environment == oldParentEnvironment)
                {
                    // this instance now points to newEnvironment
                    instance.environment = newEnvironment;
                }

                return true;
            }

            ActivityUtilities.ProcessActivityInstanceTree(currentInstance, null, processInstanceCallback);
        }

        /// <summary>
        /// Enqueues the variable default.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="variable">The variable.</param>
        /// <param name="variableLocation">The variable location.</param>
        private void EnqueueVariableDefault(ActivityExecutor executor, Variable variable, Location variableLocation)
        {
            // Incomplete initialization detection logic relies on the fact that we don't specify a
            // completion callback. If this changes we need to modify callers of SetInitializationIncomplete().
            Fx.Assert(variable.Default != null, "If we've gone async we must have a default");
            if (variableLocation == null)
            {
                variableLocation = this.environment.GetSpecificLocation(variable.Id);
            }

            variable.SetIsWaitingOnDefaultValue(variableLocation);
            executor.ScheduleExpression(variable.Default, this, this.environment, variableLocation, null);
        }

        /// <summary>
        /// Ensures the extended data.
        /// </summary>
        private void EnsureExtendedData()
        {
            if (this.SerializedExtendedData == null)
            {
                this.SerializedExtendedData = new ExtendedData();
            }
        }

        /// <summary>
        /// Generates the instance map callback.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="executor">The executor.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool GenerateInstanceMapCallback(ActivityInstance instance, ActivityExecutor executor)
        {
            this.InstanceMap.AddEntry(instance);
            instance.InstanceMap = this.InstanceMap;

            if (instance.HasActivityReferences)
            {
                instance.SerializedExtendedData.FillInstanceMap(instance.InstanceMap);
            }

            return true;
        }

        /// <summary>
        /// Internals the try populate argument value or schedule expression.
        /// </summary>
        /// <param name="argument">The argument.</param>
        /// <param name="nextArgumentIndex">Index of the next argument.</param>
        /// <param name="executor">The executor.</param>
        /// <param name="argumentValueOverrides">The argument value overrides.</param>
        /// <param name="resultLocation">The result location.</param>
        /// <param name="isDynamicUpdate">if set to <c>true</c> [is dynamic update].</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool InternalTryPopulateArgumentValueOrScheduleExpression(
            RuntimeArgument argument,
            int nextArgumentIndex,
            ActivityExecutor executor,
            IDictionary<string, object> argumentValueOverrides,
            Location resultLocation,
            bool isDynamicUpdate)
        {
            object overrideValue = null;
            if (argumentValueOverrides != null)
            {
                argumentValueOverrides.TryGetValue(argument.Name, out overrideValue);
            }

            if (argument.TryPopulateValue(this.environment, this, executor, overrideValue, resultLocation, isDynamicUpdate))
            {
                return true;
            }

            ResolveNextArgumentWorkItem workItem = null;
            var location = this.environment.GetSpecificLocation(argument.Id);

            if (isDynamicUpdate)
            {
                //1. Check if this argument has a temporary location that needs to be collapsed
                if (location.TemporaryResolutionEnvironment != null)
                {
                    // 2. Add a workitem to collapse the temporary location
                    executor.ScheduleItem(new CollapseTemporaryResolutionLocationWorkItem(location, this));
                }
            }
            else
            {
                //1. Check if there are more arguments to process
                nextArgumentIndex += 1;

                // 2. Add a workitem to resume argument resolution when work related to 3 below
                // either completes or it hits an async point.
                var totalArgumentCount = this.Activity.RuntimeArguments.Count;

                if (nextArgumentIndex < totalArgumentCount)
                {
                    workItem = executor.ResolveNextArgumentWorkItemPool.Acquire();
                    workItem.Initialize(this, nextArgumentIndex, argumentValueOverrides, resultLocation);
                }
            }

            // 3. Schedule the argument expression.
            executor.ScheduleExpression(argument.BoundArgument.Expression, this, this.Environment, location, workItem);

            return false;
        }

        /// <summary>
        /// Marks the executed.
        /// </summary>
        private void MarkExecuted() => this.SerializedSubstate = Substate.Executing;

        /// <summary>
        /// Resolves the variable.
        /// </summary>
        /// <param name="variable">The variable.</param>
        /// <param name="executor">The executor.</param>
        /// <returns><c>true</c> if completed synchronously, <c>false</c> otherwise.</returns>
        private bool ResolveVariable(Variable variable, ActivityExecutor executor)
        {
            var completedSynchronously = true;
            if (variable.Default != null)
            {
                var variableLocation = this.Environment.GetSpecificLocation(variable.Id);

                if (variable.Default.UseOldFastPath)
                {
                    variable.PopulateDefault(executor, this, variableLocation);
                }
                else
                {
                    this.EnqueueVariableDefault(executor, variable, variableLocation);
                    completedSynchronously = false;
                }
            }

            return completedSynchronously;
        }

        /// <summary>
        /// Sets the canceled.
        /// </summary>
        private void SetCanceled()
        {
            Fx.Assert(!this.IsCompleted, "Should not be completed if we are changing the state.");

            this.TryCancelParent();

            this.State = ActivityInstanceState.Canceled;
        }

        /// <summary>
        /// Sets the closed.
        /// </summary>
        private void SetClosed()
        {
            Fx.Assert(!this.IsCompleted, "Should not be completed if we are changing the state.");

            this.State = ActivityInstanceState.Closed;
        }

        /// <summary>
        /// Tries the cancel parent.
        /// </summary>
        private void TryCancelParent()
        {
            if (this.Parent != null && this.Parent.IsPerformingDefaultCancelation)
            {
                this.Parent.MarkCanceled();
            }
        }
    }
}
