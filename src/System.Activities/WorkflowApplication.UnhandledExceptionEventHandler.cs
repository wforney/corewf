// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Internals;
    using System.Threading;

    public sealed partial class WorkflowApplication
    {
        private class UnhandledExceptionEventHandler
        {
            private Func<IAsyncResult, WorkflowApplication, bool, bool> stage1Callback;

            public UnhandledExceptionEventHandler()
            {
            }

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

            public bool Run(WorkflowApplication instance, Exception exception, Activity exceptionSource, string exceptionSourceInstanceId)
            {
                IAsyncResult result = null;

                if (instance.Controller.HasPendingTrackingRecords)
                {
                    instance.EventData.NextCallback = this.Stage1Callback;
                    instance.EventData.UnhandledException = exception;
                    instance.EventData.UnhandledExceptionSource = exceptionSource;
                    instance.EventData.UnhandledExceptionSourceInstance = exceptionSourceInstanceId;
                    result = instance.Controller.BeginFlushTrackingRecords(ActivityDefaults.TrackingTimeout, EventFrameCallback, instance.EventData);

                    if (!result.CompletedSynchronously)
                    {
                        return false;
                    }
                }

                return this.OnStage1Complete(result, instance, exception, exceptionSource, exceptionSourceInstanceId);
            }

            private bool OnStage1Complete(IAsyncResult lastResult, WorkflowApplication instance, bool isStillSync) => this.OnStage1Complete(lastResult, instance, instance.EventData.UnhandledException, instance.EventData.UnhandledExceptionSource, instance.EventData.UnhandledExceptionSourceInstance);

            private bool OnStage1Complete(IAsyncResult lastResult, WorkflowApplication instance, Exception exception, Activity source, string sourceInstanceId)
            {
                if (lastResult != null)
                {
                    instance.Controller.EndFlushTrackingRecords(lastResult);
                }

                var handler = instance.OnUnhandledException;

                var action = UnhandledExceptionAction.Terminate;

                if (handler != null)
                {
                    try
                    {
                        instance.isInHandler = true;
                        instance.handlerThreadId = Thread.CurrentThread.ManagedThreadId;

                        action = handler(new WorkflowApplicationUnhandledExceptionEventArgs(instance, exception, source, sourceInstanceId));
                    }
                    finally
                    {
                        instance.isInHandler = false;
                    }
                }

                if (instance.invokeCompletedCallback != null)
                {
                    action = UnhandledExceptionAction.Terminate;
                }

                if (TD.WorkflowApplicationUnhandledExceptionIsEnabled())
                {
                    TD.WorkflowApplicationUnhandledException(instance.Id.ToString(), source.GetType().ToString(), source.DisplayName, action.ToString(), exception);
                }

                switch (action)
                {
                    case UnhandledExceptionAction.Abort:
                        instance.AbortInstance(exception, true);
                        break;

                    case UnhandledExceptionAction.Cancel:
                        instance.Controller.ScheduleCancel();
                        break;

                    case UnhandledExceptionAction.Terminate:
                        instance.TerminateCore(exception);
                        break;

                    default:
                        throw FxTrace.Exception.AsError(new InvalidOperationException(SR.InvalidUnhandledExceptionAction));
                }

                return true;
            }
        }
    }
}