// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Internals;
    using System.Activities.Tracking;
    using System.Threading;

    public sealed partial class WorkflowApplication
    {
        private class IdleEventHandler
        {
            private Func<IAsyncResult, WorkflowApplication, bool, bool> stage1Callback;
            private Func<IAsyncResult, WorkflowApplication, bool, bool> stage2Callback;

            public IdleEventHandler()
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

            public bool Run(WorkflowApplication instance)
            {
                IAsyncResult result = null;

                if (instance.Controller.TrackingEnabled)
                {
                    instance.Controller.Track(new WorkflowInstanceRecord(instance.Id, instance.WorkflowDefinition.DisplayName, WorkflowInstanceStates.Idle, instance.DefinitionIdentity));

                    instance.EventData.NextCallback = this.Stage1Callback;
                    result = instance.Controller.BeginFlushTrackingRecords(ActivityDefaults.TrackingTimeout, EventFrameCallback, instance.EventData);

                    if (!result.CompletedSynchronously)
                    {
                        return false;
                    }
                }

                return this.OnStage1Complete(result, instance, true);
            }

            private bool OnStage1Complete(IAsyncResult lastResult, WorkflowApplication application, bool isStillSync)
            {
                if (lastResult != null)
                {
                    application.Controller.EndFlushTrackingRecords(lastResult);
                }

                IAsyncResult result = null;

                if (application.RaiseIdleEvent())
                {
                    if (application.Controller.IsPersistable && application.persistenceManager != null)
                    {
                        var persistableIdleHandler = application.PersistableIdle;

                        if (persistableIdleHandler != null)
                        {
                            var action = PersistableIdleAction.None;

                            application.handlerThreadId = Thread.CurrentThread.ManagedThreadId;

                            try
                            {
                                application.isInHandler = true;
                                action = persistableIdleHandler(new WorkflowApplicationIdleEventArgs(application));
                            }
                            finally
                            {
                                application.isInHandler = false;
                            }

                            if (TD.WorkflowApplicationPersistableIdleIsEnabled())
                            {
                                TD.WorkflowApplicationPersistableIdle(application.Id.ToString(), action.ToString());
                            }

                            if (action != PersistableIdleAction.None)
                            {
                                var operation = PersistenceOperation.Unload;

                                if (action == PersistableIdleAction.Persist)
                                {
                                    operation = PersistenceOperation.Save;
                                }
                                else if (action != PersistableIdleAction.Unload)
                                {
                                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.InvalidIdleAction));
                                }

                                application.EventData.NextCallback = this.Stage2Callback;
                                result = application.BeginInternalPersist(operation, ActivityDefaults.InternalSaveTimeout, true, EventFrameCallback, application.EventData);

                                if (!result.CompletedSynchronously)
                                {
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            // Trace the default action
                            if (TD.WorkflowApplicationPersistableIdleIsEnabled())
                            {
                                TD.WorkflowApplicationPersistableIdle(application.Id.ToString(), PersistableIdleAction.None.ToString());
                            }
                        }
                    }
                }

                return this.OnStage2Complete(result, application, isStillSync);
            }

            private bool OnStage2Complete(IAsyncResult lastResult, WorkflowApplication instance, bool isStillSync)
            {
                if (lastResult != null)
                {
                    instance.EndInternalPersist(lastResult);
                }

                return true;
            }
        }
    }
}