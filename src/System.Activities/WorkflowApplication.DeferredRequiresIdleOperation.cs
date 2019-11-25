// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Activities.Hosting;

    public sealed partial class WorkflowApplication
    {
        private class DeferredRequiresIdleOperation : InstanceOperation
        {
            public DeferredRequiresIdleOperation() => this.InterruptsScheduler = false;

            public override bool CanRun(WorkflowApplication instance) => (this.ActionId != instance.actionCount && instance.Controller.State == WorkflowInstanceState.Idle) || instance.Controller.State == WorkflowInstanceState.Complete;
        }
    }
}
