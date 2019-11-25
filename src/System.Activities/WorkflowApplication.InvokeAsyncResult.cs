// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Hosting;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Collections.Generic;
    using System.Threading;

    public sealed partial class WorkflowApplication
    {
        /// <summary>
        /// The InvokeAsyncResult class.
        /// Implements the <see cref="System.Activities.Runtime.AsyncResult" />
        /// </summary>
        /// <seealso cref="System.Activities.Runtime.AsyncResult" />
        private class InvokeAsyncResult : AsyncResult
        {
            /// <summary>
            /// The wait complete callback
            /// </summary>
            private static Action<object, TimeoutException>? waitCompleteCallback;
            /// <summary>
            /// The completion waiter
            /// </summary>
            private readonly AsyncWaitHandle completionWaiter;
            /// <summary>
            /// The completion exception
            /// </summary>
            private Exception? completionException;
            /// <summary>
            /// The instance
            /// </summary>
            private WorkflowApplication instance;
            /// <summary>
            /// The outputs
            /// </summary>
            private IDictionary<string, object>? outputs;

            /// <summary>
            /// Initializes a new instance of the <see cref="InvokeAsyncResult"/> class.
            /// </summary>
            /// <param name="activity">The activity.</param>
            /// <param name="inputs">The inputs.</param>
            /// <param name="extensions">The extensions.</param>
            /// <param name="timeout">The timeout.</param>
            /// <param name="syncContext">The synchronize context.</param>
            /// <param name="invokeContext">The invoke context.</param>
            /// <param name="callback">The callback.</param>
            /// <param name="state">The state.</param>
            public InvokeAsyncResult(
                Activity activity, 
                IDictionary<string, object> inputs,
                WorkflowInstanceExtensionManager extensions, 
                TimeSpan timeout, 
                SynchronizationContext syncContext, 
                AsyncInvokeContext invokeContext, 
                AsyncCallback callback, 
                object state)
                : base(callback, state)
            {
                if (activity == null)
                {
                    throw new ArgumentNullException(nameof(activity));
                }

                this.completionWaiter = new AsyncWaitHandle();
                syncContext ??= SynchronousSynchronizationContext.Value;

                this.instance = WorkflowApplication.StartInvoke(activity, inputs, extensions, syncContext, new Action(this.OnInvokeComplete), invokeContext);

                if (this.completionWaiter.WaitAsync(WaitCompleteCallback, this, timeout))
                {
                    var completeSelf = this.OnWorkflowCompletion();

                    if (completeSelf)
                    {
                        if (this.completionException != null)
                        {
                            throw FxTrace.Exception.AsError(this.completionException);
                        }
                        else
                        {
                            this.Complete(true);
                        }
                    }
                }
            }

            private static Action<object, TimeoutException> WaitCompleteCallback
            {
                get
                {
                    if (waitCompleteCallback == null)
                    {
                        waitCompleteCallback = new Action<object, TimeoutException>(OnWaitComplete);
                    }

                    return waitCompleteCallback;
                }
            }

            public static IDictionary<string, object>? End(IAsyncResult result)
            {
                var thisPtr = AsyncResult.End<InvokeAsyncResult>(result);
                return thisPtr.outputs;
            }

            private static void OnWaitComplete(object state, TimeoutException asyncException)
            {
                var thisPtr = (InvokeAsyncResult)state;

                if (asyncException != null)
                {
                    thisPtr.instance.Abort(SR.AbortingDueToInstanceTimeout);
                    thisPtr.Complete(false, asyncException);
                    return;
                }

                var completeSelf = true;

                try
                {
                    completeSelf = thisPtr.OnWorkflowCompletion();
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    thisPtr.completionException = e;
                }

                if (completeSelf)
                {
                    thisPtr.Complete(false, thisPtr.completionException);
                }
            }

            private void OnInvokeComplete() => this.completionWaiter.Set();

            private bool OnWorkflowCompletion()
            {
                if (this.instance.Controller.State == WorkflowInstanceState.Aborted)
                {
                    this.completionException = new WorkflowApplicationAbortedException(SR.DefaultAbortReason, this.instance.Controller.GetAbortReason());
                }
                else
                {
                    Fx.Assert(this.instance.Controller.State == WorkflowInstanceState.Complete, "We should only get here when we are completed.");

                    this.instance.Controller.GetCompletionState(out this.outputs, out this.completionException);
                }

                return true;
            }
        }
    }
}