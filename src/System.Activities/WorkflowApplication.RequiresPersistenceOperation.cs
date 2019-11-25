// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Activities.Hosting;

    public sealed partial class WorkflowApplication
    {
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
    }
}