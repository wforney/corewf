// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Threading;

    public sealed partial class WorkflowApplication
    {
        /// <summary>
        /// this class is not a general purpose SyncContext and is only meant to work for workflow
        /// scenarios, where the scheduler ensures at most one work item pending. The scheduler
        /// ensures that Invoke must run before Post is called on a different thread.
        /// </summary>
        private class PumpBasedSynchronizationContext : SynchronizationContext
        {
            // The waitObject is cached per thread so that we can avoid the cost of creating events
            // for multiple synchronous invokes.
            [ThreadStatic]
            private static AutoResetEvent waitObject;

            private readonly object thisLock;
            private WorkItem currentWorkItem;
            private AutoResetEvent queueWaiter;
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

            public override void Post(SendOrPostCallback d, object state) => this.ScheduleWorkItem(new WorkItem(d, state));

            public override void Send(SendOrPostCallback d, object state) => throw FxTrace.Exception.AsError(new NotSupportedException(SR.SendNotSupported));

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
    }
}