// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;

    public sealed partial class WorkflowApplication
    {
        private class ResumeBookmarkAsyncResult : AsyncResult
        {
            private static readonly AsyncCompletion trackingCompleteCallback = new AsyncCompletion(OnTrackingComplete);
            private static readonly Action<object, TimeoutException> waitCompleteCallback = new Action<object, TimeoutException>(OnWaitComplete);
            private static AsyncCompletion resumedCallback = new AsyncCompletion(OnResumed);
            private readonly Bookmark bookmark;
            private readonly bool isFromExtension;
            private readonly object value;
            private InstanceOperation currentOperation;
            private WorkflowApplication instance;
            private bool pendedUnenqueued;
            private BookmarkResumptionResult resumptionResult;
            private TimeoutHelper timeoutHelper;

            public ResumeBookmarkAsyncResult(WorkflowApplication instance, Bookmark bookmark, object value, TimeSpan timeout, AsyncCallback callback, object state)
                : this(instance, bookmark, value, false, timeout, callback, state)
            {
            }

            public ResumeBookmarkAsyncResult(WorkflowApplication instance, Bookmark bookmark, object value, bool isFromExtension, TimeSpan timeout, AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.instance = instance;
                this.bookmark = bookmark;
                this.value = value;
                this.isFromExtension = isFromExtension;
                this.timeoutHelper = new TimeoutHelper(timeout);

                var completeSelf = false;
                var success = false;

                this.OnCompleting = new Action<AsyncResult, Exception>(this.Finally);

                try
                {
                    if (!this.instance.hasCalledRun && !this.isFromExtension)
                    {
                        // Increment the pending unenqueued count so we don't raise idle in the time
                        // between when the Run completes and when we enqueue our InstanceOperation.
                        this.pendedUnenqueued = true;
                        this.instance.IncrementPendingUnenqueud();

                        var result = this.instance.BeginInternalRun(this.timeoutHelper.RemainingTime(), false, this.PrepareAsyncCompletion(resumedCallback), this);
                        if (result.CompletedSynchronously)
                        {
                            completeSelf = OnResumed(result);
                        }
                    }
                    else
                    {
                        completeSelf = this.StartResumptionLoop();
                    }

                    success = true;
                }
                finally
                {
                    // We only want to call this if we are throwing. Otherwise OnCompleting will
                    // take care of it.
                    if (!success)
                    {
                        this.Finally(null, null);
                    }
                }

                if (completeSelf)
                {
                    this.Complete(true);
                }
            }

            public static BookmarkResumptionResult End(IAsyncResult result)
            {
                var thisPtr = AsyncResult.End<ResumeBookmarkAsyncResult>(result);

                return thisPtr.resumptionResult;
            }

            private static bool OnResumed(IAsyncResult result)
            {
                var thisPtr = (ResumeBookmarkAsyncResult)result.AsyncState;
                thisPtr.instance.EndRun(result);
                return thisPtr.StartResumptionLoop();
            }

            private static bool OnTrackingComplete(IAsyncResult result)
            {
                var thisPtr = (ResumeBookmarkAsyncResult)result.AsyncState;

                thisPtr.instance.Controller.EndFlushTrackingRecords(result);

                return true;
            }

            private static void OnWaitComplete(object state, TimeoutException asyncException)
            {
                var thisPtr = (ResumeBookmarkAsyncResult)state;

                if (asyncException != null)
                {
                    thisPtr.Complete(false, asyncException);
                    return;
                }

                Exception completionException = null;
                var completeSelf = false;

                try
                {
                    thisPtr.ClearPendedUnenqueued();

                    if (thisPtr.CheckIfBookmarksAreInvalid())
                    {
                        completeSelf = true;
                    }
                    else
                    {
                        completeSelf = thisPtr.ProcessResumption();

                        if (thisPtr.resumptionResult == BookmarkResumptionResult.NotReady)
                        {
                            completeSelf = thisPtr.WaitOnCurrentOperation();
                        }
                    }
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    completeSelf = true;
                    completionException = e;
                }

                if (completeSelf)
                {
                    thisPtr.Complete(false, completionException);
                }
            }

            private bool CheckIfBookmarksAreInvalid()
            {
                if (this.instance.AreBookmarksInvalid(out this.resumptionResult))
                {
                    return true;
                }

                return false;
            }

            private void ClearPendedUnenqueued()
            {
                if (this.pendedUnenqueued)
                {
                    this.pendedUnenqueued = false;
                    this.instance.DecrementPendingUnenqueud();
                }
            }

            private void Finally(AsyncResult result, Exception completionException)
            {
                this.ClearPendedUnenqueued();
                this.NotifyOperationComplete();
            }

            private void NotifyOperationComplete()
            {
                var lastOperation = this.currentOperation;
                this.currentOperation = null;
                this.instance.NotifyOperationComplete(lastOperation);
            }

            private bool ProcessResumption()
            {
                var stillSync = true;

                this.resumptionResult = this.instance.ResumeBookmarkCore(this.bookmark, this.value);

                if (this.resumptionResult == BookmarkResumptionResult.Success)
                {
                    if (this.instance.Controller.HasPendingTrackingRecords)
                    {
                        var result = this.instance.Controller.BeginFlushTrackingRecords(this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(trackingCompleteCallback), this);

                        if (result.CompletedSynchronously)
                        {
                            stillSync = OnTrackingComplete(result);
                        }
                        else
                        {
                            stillSync = false;
                        }
                    }
                }
                else if (this.resumptionResult == BookmarkResumptionResult.NotReady)
                {
                    this.NotifyOperationComplete();
                    this.currentOperation = new DeferredRequiresIdleOperation();
                }

                return stillSync;
            }

            private bool StartResumptionLoop()
            {
                this.currentOperation = new RequiresIdleOperation(this.isFromExtension);
                return this.WaitOnCurrentOperation();
            }

            private bool WaitOnCurrentOperation()
            {
                var stillSync = true;
                var tryOneMore = true;

                while (tryOneMore)
                {
                    tryOneMore = false;

                    Fx.Assert(this.currentOperation != null, "We should always have a current operation here.");

                    if (this.instance.WaitForTurnAsync(this.currentOperation, this.timeoutHelper.RemainingTime(), waitCompleteCallback, this))
                    {
                        this.ClearPendedUnenqueued();

                        if (this.CheckIfBookmarksAreInvalid())
                        {
                            stillSync = true;
                        }
                        else
                        {
                            stillSync = this.ProcessResumption();

                            tryOneMore = this.resumptionResult == BookmarkResumptionResult.NotReady;
                        }
                    }
                    else
                    {
                        stillSync = false;
                    }
                }

                return stillSync;
            }
        }
    }
}