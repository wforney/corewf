// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    using System;
    using System.Transactions;

    internal partial class ActivityExecutor
    {
        /// <summary>
        /// This class is not DataContract since we only create instances of it while we are in
        /// no-persist zones
        /// </summary>
        private class RuntimeTransactionData
        {
            public RuntimeTransactionData(RuntimeTransactionHandle handle, Transaction transaction, ActivityInstance isolationScope)
            {
                this.TransactionHandle = handle;
                this.OriginalTransaction = transaction;
                this.ClonedTransaction = transaction.Clone();
                this.IsolationScope = isolationScope;
                this.TransactionStatus = TransactionStatus.Active;
            }

            public AsyncWaitHandle? CompletionEvent
            {
                get;
                set;
            }

            public PreparingEnlistment? PendingPreparingEnlistment
            {
                get;
                set;
            }

            public bool HasPrepared
            {
                get;
                set;
            }

            public bool ShouldScheduleCompletion
            {
                get;
                set;
            }

            public TransactionStatus TransactionStatus
            {
                get;
                set;
            }

            public bool IsRootCancelPending
            {
                get;
                set;
            }

            public RuntimeTransactionHandle TransactionHandle
            {
                get;
                private set;
            }

            public Transaction ClonedTransaction
            {
                get;
                private set;
            }

            public Transaction OriginalTransaction
            {
                get;
                private set;
            }

            public ActivityInstance IsolationScope
            {
                get;
                private set;
            }

            [Fx.Tag.Throws(typeof(Exception), "Doesn't handle any exceptions coming from Rollback.")]
            public void Rollback(Exception reason)
            {
                Fx.Assert(this.OriginalTransaction != null, Properties.Resources.WeAlwaysHaveAnOriginalTransaction);
                if (this.OriginalTransaction == null)
                {
                    throw new InvalidOperationException();
                }
                this.OriginalTransaction.Rollback(reason);
            }
        }
    }
}
