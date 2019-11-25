// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;

    public sealed partial class WorkflowApplication
    {
        private class CancelAsyncResult : SimpleOperationAsyncResult
        {
            private CancelAsyncResult(WorkflowApplication instance, AsyncCallback callback, object state)
                : base(instance, callback, state)
            {
            }

            public static CancelAsyncResult Create(WorkflowApplication instance, TimeSpan timeout, AsyncCallback callback, object state)
            {
                var result = new CancelAsyncResult(instance, callback, state);
                result.Run(timeout);
                return result;
            }

            public static void End(IAsyncResult result) => AsyncResult.End<CancelAsyncResult>(result);

            protected override void PerformOperation() => this.Instance.CancelCore();

            protected override void ValidateState() => this.Instance.ValidateStateForCancel();
        }
    }
}
