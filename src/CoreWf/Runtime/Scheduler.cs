// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Runtime
{
    using System;
    using System.Activities.Internals;
    using System.Runtime.Serialization;
    using System.Threading;

    /// <summary>
    /// The Scheduler class.
    /// </summary>
    [DataContract(Name = XD.Runtime.Scheduler, Namespace = XD.Runtime.Namespace)]
    internal class Scheduler
    {
        /// <summary>
        /// The continue action
        /// </summary>
        private static readonly ContinueAction continueAction = new ContinueAction();

        /// <summary>
        /// The yield silently action
        /// </summary>
        private static readonly YieldSilentlyAction yieldSilentlyAction = new YieldSilentlyAction();

        /// <summary>
        /// The abort action
        /// </summary>
        private static readonly AbortAction abortAction = new AbortAction();

        /// <summary>
        /// The on scheduled work callback
        /// </summary>
        private static readonly SendOrPostCallback onScheduledWorkCallback = Fx.ThunkCallback(new SendOrPostCallback(OnScheduledWork));

        /// <summary>
        /// The synchronization context
        /// </summary>
        private SynchronizationContext synchronizationContext;

        /// <summary>
        /// The is pausing
        /// </summary>
        private bool isPausing;

        /// <summary>
        /// The resume trace required
        /// </summary>
        private bool resumeTraceRequired;

        /// <summary>
        /// The callbacks
        /// </summary>
        private Callbacks callbacks;

        /// <summary>
        /// The work item queue
        /// </summary>
        private Quack<WorkItem> workItemQueue;

        /// <summary>
        /// Initializes a new instance of the <see cref="Scheduler"/> class.
        /// </summary>
        /// <param name="callbacks">The callbacks.</param>
        public Scheduler(Callbacks callbacks) => this.Initialize(callbacks);

        /// <summary>
        /// Gets the continue.
        /// </summary>
        /// <value>The continue.</value>
        public static RequestedAction Continue => continueAction;

        /// <summary>
        /// Gets the yield silently.
        /// </summary>
        /// <value>The yield silently.</value>
        public static RequestedAction YieldSilently => yieldSilentlyAction;

        /// <summary>
        /// Gets the abort.
        /// </summary>
        /// <value>The abort.</value>
        public static RequestedAction Abort => abortAction;

        /// <summary>
        /// Gets a value indicating whether this instance is running.
        /// </summary>
        /// <value><c>true</c> if this instance is running; otherwise, <c>false</c>.</value>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance is idle.
        /// </summary>
        /// <value><c>true</c> if this instance is idle; otherwise, <c>false</c>.</value>
        public bool IsIdle => this.SerializedFirstWorkItem == null;

        /// <summary>
        /// Gets or sets the serialized first work item.
        /// </summary>
        /// <value>The serialized first work item.</value>
        [DataMember(EmitDefaultValue = false, Name = "firstWorkItem")]
        internal WorkItem SerializedFirstWorkItem { get; set; }

        /// <summary>
        /// Gets or sets the serialized work item queue.
        /// </summary>
        /// <value>The serialized work item queue.</value>
        [DataMember(EmitDefaultValue = false)]
        //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode)]
        internal WorkItem[] SerializedWorkItemQueue
        {
            get => this.workItemQueue != null && this.workItemQueue.Count > 0 ? this.workItemQueue.ToArray() : null;
            set
            {
                Fx.Assert(value != null, "EmitDefaultValue is false so we should never get null.");

                // this.firstWorkItem is serialized out separately, so don't use ScheduleWork() here
                this.workItemQueue = new Quack<WorkItem>(value);
            }
        }

        /// <summary>
        /// Fills the instance map.
        /// </summary>
        /// <param name="instanceMap">The instance map.</param>
        public void FillInstanceMap(ActivityInstanceMap instanceMap)
        {
            if (this.SerializedFirstWorkItem != null)
            {
                if (this.SerializedFirstWorkItem is ActivityInstanceMap.IActivityReference activityReference)
                {
                    instanceMap.AddEntry(activityReference, true);
                }

                if (this.workItemQueue != null && this.workItemQueue.Count > 0)
                {
                    for (var i = 0; i < this.workItemQueue.Count; i++)
                    {
                        activityReference = this.workItemQueue[i] as ActivityInstanceMap.IActivityReference;
                        if (activityReference != null)
                        {
                            instanceMap.AddEntry(activityReference, true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates the notify unhandled exception action.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="sourceInstance">The source instance.</param>
        /// <returns>RequestedAction.</returns>
        public static RequestedAction CreateNotifyUnhandledExceptionAction(Exception exception, ActivityInstance sourceInstance) =>
            new NotifyUnhandledExceptionAction(exception, sourceInstance);

        /// <summary>
        /// Clears all work items.
        /// </summary>
        /// <param name="executor">The executor.</param>
        public void ClearAllWorkItems(ActivityExecutor executor)
        {
            if (this.SerializedFirstWorkItem != null)
            {
                this.SerializedFirstWorkItem.Release(executor);
                this.SerializedFirstWorkItem = null;

                if (this.workItemQueue != null)
                {
                    while (this.workItemQueue.Count > 0)
                    {
                        var item = this.workItemQueue.Dequeue();
                        item.Release(executor);
                    }
                }
            }

            Fx.Assert(
                this.workItemQueue == null || this.workItemQueue.Count == 0,
                "We either didn't have a first work item and therefore don't have anything in the queue, or we drained the queue.");

            // For consistency we set this to null even if it is empty
            this.workItemQueue = null;
        }

        /// <summary>
        /// Called when [deserialized].
        /// </summary>
        /// <param name="callbacks">The callbacks.</param>
        public void OnDeserialized(Callbacks callbacks)
        {
            this.Initialize(callbacks);
            Fx.Assert(
                this.SerializedFirstWorkItem != null || this.workItemQueue == null,
                "cannot have items in the queue unless we also have a firstWorkItem set");
        }

        /// <summary>
        /// Internals the resume.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <remarks>This method should only be called when we relinquished the thread but did not
        /// complete the operation (silent yield is the current example)</remarks>
        public void InternalResume(RequestedAction action)
        {
            Fx.Assert(this.IsRunning, "We should still be processing work - we just don't have a thread");

            var isTracingEnabled = FxTrace.ShouldTraceInformation;
            var notifiedCompletion = false;
            var isInstanceComplete = false;

            if (this.callbacks.IsAbortPending)
            {
                this.isPausing = false;
                this.IsRunning = false;

                this.NotifyWorkCompletion();
                notifiedCompletion = true;

                if (isTracingEnabled)
                {
                    isInstanceComplete = this.callbacks.IsCompleted;
                }

                // After calling SchedulerIdle we no longer have the lock.  That means
                // that any subsequent processing in this method won't have the single
                // threaded guarantee.
                this.callbacks.SchedulerIdle();
            }
            else if (object.ReferenceEquals(action, continueAction))
            {
                this.ScheduleWork(false);
            }
            else
            {
                Fx.Assert(action is NotifyUnhandledExceptionAction, "This is the only other choice because we should never have YieldSilently here");

                var notifyAction = (NotifyUnhandledExceptionAction)action;

                // We only set isRunning back to false so that the host doesn't
                // have to treat this like a pause notification.  As an example,
                // a host could turn around and call run again in response to
                // UnhandledException without having to go through its operation
                // dispatch loop first (or request pause again).  If we reset
                // isPausing here then any outstanding operations wouldn't get
                // signaled with that type of host.
                this.IsRunning = false;

                this.NotifyWorkCompletion();
                notifiedCompletion = true;

                if (isTracingEnabled)
                {
                    isInstanceComplete = this.callbacks.IsCompleted;
                }

                this.callbacks.NotifyUnhandledException(notifyAction.Exception, notifyAction.Source);
            }

            if (isTracingEnabled)
            {
                if (notifiedCompletion)
                {
                    var oldActivityId = Guid.Empty;
                    var resetId = false;

                    if (isInstanceComplete)
                    {
                        if (TD.WorkflowActivityStopIsEnabled())
                        {
                            oldActivityId = WfEventSource.CurrentThreadActivityId;
                            WfEventSource.SetCurrentThreadActivityId(this.callbacks.WorkflowInstanceId);
                            resetId = true;

                            TD.WorkflowActivityStop(this.callbacks.WorkflowInstanceId);
                        }
                    }
                    else
                    {
                        if (TD.WorkflowActivitySuspendIsEnabled())
                        {
                            oldActivityId = WfEventSource.CurrentThreadActivityId;
                            WfEventSource.SetCurrentThreadActivityId(this.callbacks.WorkflowInstanceId);
                            resetId = true;

                            TD.WorkflowActivitySuspend(this.callbacks.WorkflowInstanceId);
                        }
                    }

                    if (resetId)
                    {
                        WfEventSource.SetCurrentThreadActivityId(oldActivityId);
                    }
                }
            }
        }

        /// <summary>
        /// Initializes the specified callbacks.
        /// </summary>
        /// <param name="callbacks">The callbacks.</param>
        /// <remarks>called from ctor and OnDeserialized intialization paths</remarks>
        private void Initialize(Callbacks callbacks) => this.callbacks = callbacks;

        /// <summary>
        /// Opens the specified synchronization context.
        /// </summary>
        /// <param name="synchronizationContext">The synchronization context.</param>
        public void Open(SynchronizationContext synchronizationContext)
        {
            Fx.Assert(this.synchronizationContext == null, "can only open when in the created state");
            if (synchronizationContext != null)
            {
                this.synchronizationContext = synchronizationContext;
            }
            else
            {
                this.synchronizationContext = SynchronizationContextHelper.GetDefaultSynchronizationContext();
            }
        }

        /// <summary>
        /// Opens the specified old scheduler.
        /// </summary>
        /// <param name="oldScheduler">The old scheduler.</param>
        internal void Open(Scheduler oldScheduler)
        {
            Fx.Assert(this.synchronizationContext == null, "can only open when in the created state");
            this.synchronizationContext = SynchronizationContextHelper.CloneSynchronizationContext(oldScheduler.synchronizationContext);
        }

        /// <summary>
        /// Schedules the work.
        /// </summary>
        /// <param name="notifyStart">if set to <c>true</c> [notify start].</param>
        private void ScheduleWork(bool notifyStart)
        {
            if (notifyStart)
            {
                this.synchronizationContext.OperationStarted();
                this.resumeTraceRequired = true;
            }
            else
            {
                this.resumeTraceRequired = false;
            }
            this.synchronizationContext.Post(Scheduler.onScheduledWorkCallback, this);
        }

        /// <summary>
        /// Notifies the work completion.
        /// </summary>
        private void NotifyWorkCompletion() => this.synchronizationContext.OperationCompleted();

        /// <summary>
        /// Pauses this instance.
        /// </summary>
        /// <remarks>signal the scheduler to stop processing work. If we are processing work
        /// then we will catch this signal at our next iteration. Pause process completes
        /// when idle is signalled. Can be called while we're processing work since
        /// the worst thing that could happen in a race is that we pause one extra work item later</remarks>
        public void Pause() => this.isPausing = true;

        /// <summary>
        /// Marks the running.
        /// </summary>
        public void MarkRunning() => this.IsRunning = true;

        /// <summary>
        /// Resumes this instance.
        /// </summary>
        public void Resume()
        {
            Fx.Assert(this.IsRunning, "This should only be called after we've been set to process work.");

            if (this.IsIdle || this.isPausing || this.callbacks.IsAbortPending)
            {
                this.isPausing = false;
                this.IsRunning = false;
                this.callbacks.SchedulerIdle();
            }
            else
            {
                this.ScheduleWork(true);
            }
        }

        /// <summary>
        /// Pushes the work.
        /// </summary>
        /// <param name="workItem">The work item.</param>
        public void PushWork(WorkItem workItem)
        {
            if (this.SerializedFirstWorkItem == null)
            {
                this.SerializedFirstWorkItem = workItem;
            }
            else
            {
                if (this.workItemQueue == null)
                {
                    this.workItemQueue = new Quack<WorkItem>();
                }

                this.workItemQueue.PushFront(this.SerializedFirstWorkItem);
                this.SerializedFirstWorkItem = workItem;
            }

            // To avoid the virt call on EVERY work item we check
            // the Verbose flag.  All of our Schedule traces are
            // verbose.
            if (FxTrace.ShouldTraceVerboseToTraceSource)
            {
                workItem.TraceScheduled();
            }
        }

        /// <summary>
        /// Enqueues the work.
        /// </summary>
        /// <param name="workItem">The work item.</param>
        public void EnqueueWork(WorkItem workItem)
        {
            if (this.SerializedFirstWorkItem == null)
            {
                this.SerializedFirstWorkItem = workItem;
            }
            else
            {
                if (this.workItemQueue == null)
                {
                    this.workItemQueue = new Quack<WorkItem>();
                }

                this.workItemQueue.Enqueue(workItem);
            }

            if (FxTrace.ShouldTraceVerboseToTraceSource)
            {
                workItem.TraceScheduled();
            }
        }

        /// <summary>
        /// Called when [scheduled work].
        /// </summary>
        /// <param name="state">The state.</param>
        private static void OnScheduledWork(object state)
        {
            var thisPtr = (Scheduler)state;

            // We snapshot these values here so that we can
            // use them after calling OnSchedulerIdle.
            //bool isTracingEnabled = FxTrace.Trace.ShouldTraceToTraceSource(TraceEventLevel.Informational);

            //if (isTracingEnabled)
            //{
            //    oldActivityId = DiagnosticTraceBase.ActivityId;
            //    workflowInstanceId = thisPtr.callbacks.WorkflowInstanceId;
            //    FxTrace.Trace.SetAndTraceTransfer(workflowInstanceId, true);

            //    if (thisPtr.resumeTraceRequired)
            //    {
            //        if (TD.WorkflowActivityResumeIsEnabled())
            //        {
            //            TD.WorkflowActivityResume(workflowInstanceId);
            //        }
            //    }
            //}

            thisPtr.callbacks.ThreadAcquired();

            RequestedAction nextAction = continueAction;
            var idleOrPaused = false;

            while (object.ReferenceEquals(nextAction, continueAction))
            {
                if (thisPtr.IsIdle || thisPtr.isPausing)
                {
                    idleOrPaused = true;
                    break;
                }

                // cycle through (queue->thisPtr.firstWorkItem->currentWorkItem)
                var currentWorkItem = thisPtr.SerializedFirstWorkItem;

                // promote an item out of our work queue if necessary
                if (thisPtr.workItemQueue != null && thisPtr.workItemQueue.Count > 0)
                {
                    thisPtr.SerializedFirstWorkItem = thisPtr.workItemQueue.Dequeue();
                }
                else
                {
                    thisPtr.SerializedFirstWorkItem = null;
                }

                if (TD.ExecuteWorkItemStartIsEnabled())
                {
                    TD.ExecuteWorkItemStart();
                }

                nextAction = thisPtr.callbacks.ExecuteWorkItem(currentWorkItem);

                if (TD.ExecuteWorkItemStopIsEnabled())
                {
                    TD.ExecuteWorkItemStop();
                }
            }

            if (idleOrPaused || object.ReferenceEquals(nextAction, abortAction))
            {
                thisPtr.isPausing = false;
                thisPtr.IsRunning = false;

                thisPtr.NotifyWorkCompletion();

                //if (isTracingEnabled)
                //{
                //    isInstanceComplete = thisPtr.callbacks.IsCompleted;
                //}

                // After calling SchedulerIdle we no longer have the lock.  That means
                // that any subsequent processing in this method won't have the single
                // threaded guarantee.
                thisPtr.callbacks.SchedulerIdle();
            }
            else if (!object.ReferenceEquals(nextAction, yieldSilentlyAction))
            {
                Fx.Assert(nextAction is NotifyUnhandledExceptionAction, "This is the only other option");

                var notifyAction = (NotifyUnhandledExceptionAction)nextAction;

                // We only set isRunning back to false so that the host doesn't
                // have to treat this like a pause notification.  As an example,
                // a host could turn around and call run again in response to
                // UnhandledException without having to go through its operation
                // dispatch loop first (or request pause again).  If we reset
                // isPausing here then any outstanding operations wouldn't get
                // signaled with that type of host.
                thisPtr.IsRunning = false;

                thisPtr.NotifyWorkCompletion();

                //if (isTracingEnabled)
                //{
                //    isInstanceComplete = thisPtr.callbacks.IsCompleted;
                //}

                thisPtr.callbacks.NotifyUnhandledException(notifyAction.Exception, notifyAction.Source);
            }

            //if (isTracingEnabled)
            //{
            //    if (notifiedCompletion)
            //    {
            //        if (isInstanceComplete)
            //        {
            //            if (TD.WorkflowActivityStopIsEnabled())
            //            {
            //                TD.WorkflowActivityStop(workflowInstanceId);
            //            }
            //        }
            //        else
            //        {
            //            if (TD.WorkflowActivitySuspendIsEnabled())
            //            {
            //                TD.WorkflowActivitySuspend(workflowInstanceId);
            //            }
            //        }
            //    }

            //    DiagnosticTraceBase.ActivityId = oldActivityId;
            //}
        }

        /// <summary>
        /// The Callbacks structure.
        /// </summary>
        public struct Callbacks
        {
            /// <summary>
            /// The activity executor
            /// </summary>
            private readonly ActivityExecutor activityExecutor;

            /// <summary>
            /// Initializes a new instance of the <see cref="Callbacks"/> struct.
            /// </summary>
            /// <param name="activityExecutor">The activity executor.</param>
            public Callbacks(ActivityExecutor activityExecutor) => this.activityExecutor = activityExecutor;

            /// <summary>
            /// Gets the workflow instance identifier.
            /// </summary>
            /// <value>The workflow instance identifier.</value>
            public Guid WorkflowInstanceId => this.activityExecutor.WorkflowInstanceId;

            /// <summary>
            /// Gets a value indicating whether this instance is abort pending.
            /// </summary>
            /// <value><c>true</c> if this instance is abort pending; otherwise, <c>false</c>.</value>
            public bool IsAbortPending => this.activityExecutor.IsAbortPending || this.activityExecutor.IsTerminatePending;

            /// <summary>
            /// Gets a value indicating whether this instance is completed.
            /// </summary>
            /// <value><c>true</c> if this instance is completed; otherwise, <c>false</c>.</value>
            public bool IsCompleted => ActivityUtilities.IsCompletedState(this.activityExecutor.State);

            /// <summary>
            /// Executes the work item.
            /// </summary>
            /// <param name="workItem">The work item.</param>
            /// <returns>RequestedAction.</returns>
            public RequestedAction ExecuteWorkItem(WorkItem workItem)
            {
                Fx.Assert(this.activityExecutor != null, "ActivityExecutor null in ExecuteWorkItem.");

                // We check the Verbose flag to avoid the
                // virt call if possible
                if (FxTrace.ShouldTraceVerboseToTraceSource)
                {
                    workItem.TraceStarting();
                }

                var action = this.activityExecutor.OnExecuteWorkItem(workItem);

                if (!object.ReferenceEquals(action, Scheduler.YieldSilently))
                {
                    if (this.activityExecutor.IsAbortPending || this.activityExecutor.IsTerminatePending)
                    {
                        action = Scheduler.Abort;
                    }

                    // if the caller yields, then the work item is still active and the callback
                    // is responsible for releasing it back to the pool
                    workItem.Dispose(this.activityExecutor);
                }

                return action;
            }

            /// <summary>
            /// Schedulers the idle.
            /// </summary>
            public void SchedulerIdle()
            {
                Fx.Assert(this.activityExecutor != null, "ActivityExecutor null in SchedulerIdle.");
                this.activityExecutor.OnSchedulerIdle();
            }

            /// <summary>
            /// Threads the acquired.
            /// </summary>
            public void ThreadAcquired()
            {
                Fx.Assert(this.activityExecutor != null, "ActivityExecutor null in ThreadAcquired.");
                this.activityExecutor.OnSchedulerThreadAcquired();
            }

            /// <summary>
            /// Notifies the unhandled exception.
            /// </summary>
            /// <param name="exception">The exception.</param>
            /// <param name="source">The source.</param>
            public void NotifyUnhandledException(Exception exception, ActivityInstance source)
            {
                Fx.Assert(this.activityExecutor != null, "ActivityExecutor null in NotifyUnhandledException.");
                this.activityExecutor.NotifyUnhandledException(exception, source);
            }
        }

        /// <summary>
        /// The RequestedAction class.
        /// </summary>
        internal abstract class RequestedAction
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RequestedAction"/> class.
            /// </summary>
            protected RequestedAction()
            {
            }
        }

        /// <summary>
        /// The ContinueAction class.
        /// Implements the <see cref="System.Activities.Runtime.Scheduler.RequestedAction" />
        /// </summary>
        /// <seealso cref="System.Activities.Runtime.Scheduler.RequestedAction" />
        private class ContinueAction : RequestedAction
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ContinueAction"/> class.
            /// </summary>
            public ContinueAction()
            {
            }
        }

        /// <summary>
        /// The YieldSilentlyAction class.
        /// Implements the <see cref="System.Activities.Runtime.Scheduler.RequestedAction" />
        /// </summary>
        /// <seealso cref="System.Activities.Runtime.Scheduler.RequestedAction" />
        private class YieldSilentlyAction : RequestedAction
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="YieldSilentlyAction"/> class.
            /// </summary>
            public YieldSilentlyAction()
            {
            }
        }

        /// <summary>
        /// The AbortAction class.
        /// Implements the <see cref="System.Activities.Runtime.Scheduler.RequestedAction" />
        /// </summary>
        /// <seealso cref="System.Activities.Runtime.Scheduler.RequestedAction" />
        private class AbortAction : RequestedAction
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="AbortAction"/> class.
            /// </summary>
            public AbortAction()
            {
            }
        }

        /// <summary>
        /// The NotifyUnhandledExceptionAction class.
        /// Implements the <see cref="System.Activities.Runtime.Scheduler.RequestedAction" />
        /// </summary>
        /// <seealso cref="System.Activities.Runtime.Scheduler.RequestedAction" />
        private class NotifyUnhandledExceptionAction : RequestedAction
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="NotifyUnhandledExceptionAction"/> class.
            /// </summary>
            /// <param name="exception">The exception.</param>
            /// <param name="source">The source.</param>
            public NotifyUnhandledExceptionAction(Exception exception, ActivityInstance source)
            {
                this.Exception = exception;
                this.Source = source;
            }

            /// <summary>
            /// Gets or sets the exception.
            /// </summary>
            /// <value>The exception.</value>
            public Exception Exception
            {
                get;
                private set;
            }

            /// <summary>
            /// Gets or sets the source.
            /// </summary>
            /// <value>The source.</value>
            public ActivityInstance Source
            {
                get;
                private set;
            }
        }
    }
}