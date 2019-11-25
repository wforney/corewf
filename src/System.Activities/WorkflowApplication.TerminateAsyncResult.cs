// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;

    public sealed partial class WorkflowApplication
    {
        private class TerminateAsyncResult : SimpleOperationAsyncResult
        {
            private readonly Exception reason;

            private TerminateAsyncResult(WorkflowApplication instance, Exception reason, AsyncCallback callback, object state)
                : base(instance, callback, state) => this.reason = reason;

            public static TerminateAsyncResult Create(WorkflowApplication instance, Exception reason, TimeSpan timeout, AsyncCallback callback, object state)
            {
                var result = new TerminateAsyncResult(instance, reason, callback, state);
                result.Run(timeout);
                return result;
            }

            public static void End(IAsyncResult result) => AsyncResult.End<TerminateAsyncResult>(result);

            protected override void PerformOperation() => this.Instance.TerminateCore(this.reason);

            protected override void ValidateState() => this.Instance.ValidateStateForTerminate();
        }
    }
}