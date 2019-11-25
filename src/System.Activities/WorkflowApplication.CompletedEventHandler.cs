// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;
    using System.Threading;

    public sealed partial class WorkflowApplication
    {
        /// <summary>
        /// The CompletedEventHandler class.
        /// </summary>
        private class CompletedEventHandler
        {
            /// <summary>
            /// The stage1 callback
            /// </summary>
            private Func<IAsyncResult, WorkflowApplication, bool, bool>? stage1Callback;
            /// <summary>
            /// The stage2 callback
            /// </summary>
            private Func<IAsyncResult, WorkflowApplication, bool, bool>? stage2Callback;

            /// <summary>
            /// Initializes a new instance of the <see cref="CompletedEventHandler"/> class.
            /// </summary>
            public CompletedEventHandler()
            {
            }

            /// <summary>
            /// Gets the stage1 callback.
            /// </summary>
            /// <value>The stage1 callback.</value>
            private Func<IAsyncResult, WorkflowApplication, bool, bool> Stage1Callback
            {
                get
                {
                    if (this.stage1Callback == null)
                    {
                        this.stage1Callback = new Func<IAsyncResult, WorkflowApplication, bool, bool>(this.OnStage1Complete);
                    }

                    return this.stage1Callback;
                }
            }

            /// <summary>
            /// Gets the stage2 callback.
            /// </summary>
            /// <value>The stage2 callback.</value>
            private Func<IAsyncResult, WorkflowApplication, bool, bool> Stage2Callback
            {
                get
                {
                    if (this.stage2Callback == null)
                    {
                        this.stage2Callback = new Func<IAsyncResult, WorkflowApplication, bool, bool>(this.OnStage2Complete);
                    }

                    return this.stage2Callback;
                }
            }

            /// <summary>
            /// Runs the specified instance.
            /// </summary>
            /// <param name="instance">The instance.</param>
            /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
            public bool Run(WorkflowApplication instance)
            {
                IAsyncResult? result = null;
                if (instance.Controller.HasPendingTrackingRecords)
                {
                    instance.EventData.NextCallback = this.Stage1Callback;
                    result = instance.Controller.BeginFlushTrackingRecords(ActivityDefaults.TrackingTimeout, EventFrameCallback, instance.EventData);

                    if (!result.CompletedSynchronously)
                    {
                        return false;
                    }
                }

                return this.OnStage1Complete(result, instance, true);
            }

            /// <summary>
            /// Called when [stage1 complete].
            /// </summary>
            /// <param name="lastResult">The last result.</param>
            /// <param name="instance">The instance.</param>
            /// <param name="isStillSync">if set to <c>true</c> [is still synchronize].</param>
            /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
            private bool OnStage1Complete(IAsyncResult? lastResult, WorkflowApplication instance, bool isStillSync)
            {
                if (lastResult != null)
                {
                    instance.Controller.EndFlushTrackingRecords(lastResult);
                }
                var completionState = instance.Controller.GetCompletionState(out var outputs, out var completionException);

                if (instance.invokeCompletedCallback == null)
                {
                    var handler = instance.Completed;

                    if (handler != null)
                    {
                        instance.handlerThreadId = Thread.CurrentThread.ManagedThreadId;

                        try
                        {
                            instance.isInHandler = true;
                            handler(new WorkflowApplicationCompletedEventArgs(instance, completionException, completionState, outputs));
                        }
                        finally
                        {
                            instance.isInHandler = false;
                        }
                    }
                }

                switch (completionState)
                {
                    case ActivityInstanceState.Closed:
                        if (TD.WorkflowApplicationCompletedIsEnabled())
                        {
                            TD.WorkflowApplicationCompleted(instance.Id.ToString());
                        }
                        break;

                    case ActivityInstanceState.Canceled:
                        if (TD.WorkflowInstanceCanceledIsEnabled())
                        {
                            TD.WorkflowInstanceCanceled(instance.Id.ToString());
                        }
                        break;

                    case ActivityInstanceState.Faulted:
                        if (TD.WorkflowApplicationTerminatedIsEnabled())
                        {
                            TD.WorkflowApplicationTerminated(instance.Id.ToString(), completionException);
                        }
                        break;
                }

                IAsyncResult? result = null;
                Fx.Assert(instance.Controller.IsPersistable, "Should not be in a No Persist Zone once the instance is complete.");
                if (instance.persistenceManager != null || instance.HasPersistenceModule)
                {
                    instance.EventData.NextCallback = this.Stage2Callback;
                    result = instance.BeginInternalPersist(PersistenceOperation.Unload, ActivityDefaults.InternalSaveTimeout, true, EventFrameCallback, instance.EventData);

                    if (!result.CompletedSynchronously)
                    {
                        return false;
                    }
                }
                else
                {
                    instance.MarkUnloaded();
                }

                return this.OnStage2Complete(result, instance, isStillSync);
            }

            /// <summary>
            /// Called when [stage2 complete].
            /// </summary>
            /// <param name="lastResult">The last result.</param>
            /// <param name="instance">The instance.</param>
            /// <param name="isStillSync">if set to <c>true</c> [is still synchronize].</param>
            /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
            private bool OnStage2Complete(IAsyncResult? lastResult, WorkflowApplication instance, bool isStillSync)
            {
                if (lastResult != null)
                {
                    instance.EndInternalPersist(lastResult);
                }

                instance.invokeCompletedCallback?.Invoke();

                return true;
            }
        }
    }
}