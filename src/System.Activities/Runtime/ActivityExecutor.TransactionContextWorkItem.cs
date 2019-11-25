// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    using System;
    using System.Activities.Internals;
    using System.Runtime.Serialization;

    internal partial class ActivityExecutor
    {
        [DataContract]
        internal class TransactionContextWorkItem : ActivityExecutionWorkItem
        {
            public TransactionContextWorkItem(TransactionContextWaiter waiter)
                : base(waiter.WaitingInstance)
            {
                this.SerializedWaiter = waiter;

                if (this.SerializedWaiter.IsRequires)
                {
                    this.ExitNoPersistRequired = true;
                }
            }

            [DataMember(Name = "waiter")]
            internal TransactionContextWaiter SerializedWaiter { get; set; }

            public override void TraceCompleted()
            {
                if (TD.CompleteTransactionContextWorkItemIsEnabled())
                {
                    TD.CompleteTransactionContextWorkItem(this.ActivityInstance.Activity.GetType().ToString(), this.ActivityInstance.Activity.DisplayName, this.ActivityInstance.Id);
                }
            }

            public override void TraceScheduled()
            {
                if (TD.ScheduleTransactionContextWorkItemIsEnabled())
                {
                    TD.ScheduleTransactionContextWorkItem(this.ActivityInstance.Activity.GetType().ToString(), this.ActivityInstance.Activity.DisplayName, this.ActivityInstance.Id);
                }
            }

            public override void TraceStarting()
            {
                if (TD.StartTransactionContextWorkItemIsEnabled())
                {
                    TD.StartTransactionContextWorkItem(this.ActivityInstance.Activity.GetType().ToString(), this.ActivityInstance.Activity.DisplayName, this.ActivityInstance.Id);
                }
            }

            public override bool Execute(ActivityExecutor executor, BookmarkManager bookmarkManager)
            {
                NativeActivityTransactionContext transactionContext = null;

                try
                {
                    transactionContext = new NativeActivityTransactionContext(this.ActivityInstance, executor, bookmarkManager, this.SerializedWaiter.Handle);
                    this.SerializedWaiter.CallbackWrapper.Invoke(transactionContext, this.SerializedWaiter.State);
                }
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    this.ExceptionToPropagate = e;
                }
                finally
                {
                    if (transactionContext != null)
                    {
                        transactionContext.Dispose();
                    }
                }

                return true;
            }
        }
    }
}
