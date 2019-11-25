// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;

    public sealed partial class WorkflowApplication
    {
        private abstract class SimpleOperationAsyncResult : AsyncResult
        {
            private static readonly AsyncCallback trackingCompleteCallback = Fx.ThunkCallback(new AsyncCallback(OnTrackingComplete));
            private static readonly Action<object, TimeoutException> waitCompleteCallback = new Action<object, TimeoutException>(OnWaitComplete);
            private WorkflowApplication instance;
            private TimeoutHelper timeoutHelper;

            protected SimpleOperationAsyncResult(WorkflowApplication instance, AsyncCallback callback, object state)
                : base(callback, state) => this.instance = instance;

            protected WorkflowApplication Instance => this.instance;

            protected abstract void PerformOperation();

            protected void Run(TimeSpan timeout)
            {
                this.timeoutHelper = new TimeoutHelper(timeout);

                var operation = new InstanceOperation();

                var completeSelf = true;

                try
                {
                    completeSelf = this.instance.WaitForTurnAsync(operation, this.timeoutHelper.RemainingTime(), waitCompleteCallback, this);

                    if (completeSelf)
                    {
                        this.ValidateState();

                        completeSelf = this.PerformOperationAndTrack();
                    }
                }
                finally
                {
                    if (completeSelf)
                    {
                        this.instance.NotifyOperationComplete(operation);
                    }
                }

                if (completeSelf)
                {
                    this.Complete(true);
                }
            }

            protected abstract void ValidateState();

            private static void OnTrackingComplete(IAsyncResult result)
            {
                if (result.CompletedSynchronously)
                {
                    return;
                }

                var thisPtr = (SimpleOperationAsyncResult)result.AsyncState;

                Exception completionException = null;

                try
                {
                    thisPtr.instance.Controller.EndFlushTrackingRecords(result);
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    completionException = e;
                }
                finally
                {
                    thisPtr.instance.ForceNotifyOperationComplete();
                }

                thisPtr.Complete(false, completionException);
            }

            private static void OnWaitComplete(object state, TimeoutException asyncException)
            {
                var thisPtr = (SimpleOperationAsyncResult)state;

                if (asyncException != null)
                {
                    thisPtr.Complete(false, asyncException);
                }
                else
                {
                    Exception completionException = null;
                    var completeSelf = true;

                    try
                    {
                        thisPtr.ValidateState();

                        completeSelf = thisPtr.PerformOperationAndTrack();
                    }
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                        {
                            throw;
                        }

                        completionException = e;
                    }
                    finally
                    {
                        if (completeSelf)
                        {
                            thisPtr.instance.ForceNotifyOperationComplete();
                        }
                    }

                    if (completeSelf)
                    {
                        thisPtr.Complete(false, completionException);
                    }
                }
            }

            private bool PerformOperationAndTrack()
            {
                this.PerformOperation();

                var completedSync = true;

                if (this.instance.Controller.HasPendingTrackingRecords)
                {
                    var trackingResult = this.instance.Controller.BeginFlushTrackingRecords(this.timeoutHelper.RemainingTime(), trackingCompleteCallback, this);

                    if (trackingResult.CompletedSynchronously)
                    {
                        this.instance.Controller.EndFlushTrackingRecords(trackingResult);
                    }
                    else
                    {
                        completedSync = false;
                    }
                }

                return completedSync;
            }
        }
    }
}