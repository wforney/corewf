// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;
    using System.Activities.Runtime.DurableInstancing;
    using System.Transactions;

    public sealed partial class WorkflowApplication
    {
        private class UnlockInstanceAsyncResult : TransactedAsyncResult
        {
            private static Action<AsyncResult, Exception> completeCallback = new Action<AsyncResult, Exception>(OnComplete);
            private static AsyncCompletion instanceUnlockedCallback = new AsyncCompletion(OnInstanceUnlocked);
            private static AsyncCompletion ownerDeletedCallback = new AsyncCompletion(OnOwnerDeleted);
            private readonly PersistenceManager persistenceManager;
            private readonly TimeoutHelper timeoutHelper;
            private DependentTransaction dependentTransaction;

            public UnlockInstanceAsyncResult(PersistenceManager persistenceManager, TimeoutHelper timeoutHelper, AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.persistenceManager = persistenceManager;
                this.timeoutHelper = timeoutHelper;

                var currentTransaction = Transaction.Current;
                if (currentTransaction != null)
                {
                    this.dependentTransaction = currentTransaction.DependentClone(DependentCloneOption.BlockCommitUntilComplete);
                }

                this.OnCompleting = UnlockInstanceAsyncResult.completeCallback;

                var success = false;
                try
                {
                    IAsyncResult result;
                    using (this.PrepareTransactionalCall(this.dependentTransaction))
                    {
                        if (this.persistenceManager.OwnerWasCreated)
                        {
                            // if the owner was created by this WorkflowApplication, delete it. This
                            // implicitly unlocks the instance.
                            result = this.persistenceManager.BeginDeleteOwner(this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(ownerDeletedCallback), this);
                        }
                        else
                        {
                            result = this.persistenceManager.BeginUnlock(this.timeoutHelper.RemainingTime(), this.PrepareAsyncCompletion(instanceUnlockedCallback), this);
                        }
                    }

                    if (this.SyncContinue(result))
                    {
                        this.Complete(true);
                    }

                    success = true;
                }
                finally
                {
                    if (!success)
                    {
                        this.persistenceManager.Abort();
                    }
                }
            }

            public static void End(IAsyncResult result) => AsyncResult.End<UnlockInstanceAsyncResult>(result);

            private static void OnComplete(AsyncResult result, Exception exception)
            {
                var thisPtr = (UnlockInstanceAsyncResult)result;
                if (thisPtr.dependentTransaction != null)
                {
                    thisPtr.dependentTransaction.Complete();
                }
                thisPtr.persistenceManager.Abort();
            }

            private static bool OnInstanceUnlocked(IAsyncResult result)
            {
                var thisPtr = (UnlockInstanceAsyncResult)result.AsyncState;
                thisPtr.persistenceManager.EndUnlock(result);
                return true;
            }

            private static bool OnOwnerDeleted(IAsyncResult result)
            {
                var thisPtr = (UnlockInstanceAsyncResult)result.AsyncState;
                thisPtr.persistenceManager.EndDeleteOwner(result);
                return true;
            }
        }
    }
}