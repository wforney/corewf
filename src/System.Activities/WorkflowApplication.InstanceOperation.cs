// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;

    public sealed partial class WorkflowApplication
    {
        private class InstanceOperation
        {
            private AsyncWaitHandle waitHandle;

            public InstanceOperation()
            {
                this.InterruptsScheduler = true;
                this.RequiresInitialized = true;
            }

            public int ActionId { get; set; }

            public bool InterruptsScheduler { get; protected set; }

            public bool Notified { get; set; }

            public bool RequiresInitialized { get; set; }

            public virtual bool CanRun(WorkflowApplication instance) => true;

            public void NotifyTurn()
            {
                Fx.Assert(this.waitHandle != null, "We must have a wait handle.");

                this.waitHandle?.Set();
            }

            public void OnEnqueued() => this.waitHandle = new AsyncWaitHandle();

            public bool WaitForTurn(TimeSpan timeout) => this.waitHandle == null ? true : this.waitHandle.Wait(timeout);

            public bool WaitForTurnAsync(TimeSpan timeout, Action<object, TimeoutException> callback, object state) =>
                this.waitHandle == null ? true : this.waitHandle.WaitAsync(callback, state, timeout);
        }
    }
}