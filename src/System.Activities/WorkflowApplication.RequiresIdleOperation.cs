// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Activities.Hosting;

    public sealed partial class WorkflowApplication
    {
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
    }
}