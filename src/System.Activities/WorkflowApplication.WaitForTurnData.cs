// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;

    public sealed partial class WorkflowApplication
    {
        private class WaitForTurnData
        {
            public WaitForTurnData(Action<object, TimeoutException> callback, object state, InstanceOperation operation, WorkflowApplication instance)
            {
                this.Callback = callback;
                this.State = state;
                this.Operation = operation;
                this.Instance = instance;
            }

            public Action<object, TimeoutException> Callback
            {
                get;
                private set;
            }

            public WorkflowApplication Instance
            {
                get;
                private set;
            }

            public InstanceOperation Operation
            {
                get;
                private set;
            }

            public object State
            {
                get;
                private set;
            }
        }
    }
}