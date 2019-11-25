// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;

    public sealed partial class WorkflowApplication
    {
        private class RunAsyncResult : SimpleOperationAsyncResult
        {
            private readonly bool isUserRun;

            private RunAsyncResult(WorkflowApplication instance, bool isUserRun, AsyncCallback callback, object state)
                : base(instance, callback, state) => this.isUserRun = isUserRun;

            public static RunAsyncResult Create(WorkflowApplication instance, bool isUserRun, TimeSpan timeout, AsyncCallback callback, object state)
            {
                var result = new RunAsyncResult(instance, isUserRun, callback, state);
                result.Run(timeout);
                return result;
            }

            public static void End(IAsyncResult result) => AsyncResult.End<RunAsyncResult>(result);

            protected override void PerformOperation()
            {
                if (this.isUserRun)
                {
                    // We set this to true here so that idle will be raised regardless of whether
                    // any work is performed.
                    this.Instance.hasExecutionOccurredSinceLastIdle = true;
                }

                this.Instance.RunCore();
            }

            protected override void ValidateState() => this.Instance.ValidateStateForRun();
        }
    }
}