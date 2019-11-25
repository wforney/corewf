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
        private class InstanceCommandWithTemporaryHandleAsyncResult : TransactedAsyncResult
        {
            private static AsyncCompletion commandCompletedCallback = new AsyncCompletion(OnCommandCompleted);
            private static Action<AsyncResult, Exception> completeCallback = new Action<AsyncResult, Exception>(OnComplete);
            private readonly InstancePersistenceCommand command;
            private InstanceView? commandResult;
            private DependentTransaction? dependentTransaction;
            private InstanceStore instanceStore;
            private InstanceHandle temporaryHandle;

            public InstanceCommandWithTemporaryHandleAsyncResult(InstanceStore instanceStore, InstancePersistenceCommand command,
                TimeSpan timeout, AsyncCallback callback, object state)
                : base(callback, state)
            {
                this.instanceStore = instanceStore;
                this.command = command;
                this.temporaryHandle = instanceStore.CreateInstanceHandle();

                var currentTransaction = Transaction.Current;
                if (currentTransaction != null)
                {
                    this.dependentTransaction = currentTransaction.DependentClone(DependentCloneOption.BlockCommitUntilComplete);
                }

                this.OnCompleting = completeCallback;

                IAsyncResult result;
                using (this.PrepareTransactionalCall(this.dependentTransaction))
                {
                    result = instanceStore.BeginExecute(this.temporaryHandle, command, timeout, this.PrepareAsyncCompletion(commandCompletedCallback), this);
                }

                if (this.SyncContinue(result))
                {
                    this.Complete(true);
                }
            }

            public static void End(IAsyncResult result, out InstanceStore instanceStore, out InstanceView commandResult)
            {
                var thisPtr = AsyncResult.End<InstanceCommandWithTemporaryHandleAsyncResult>(result);
                instanceStore = thisPtr.instanceStore;
                commandResult = thisPtr.commandResult;
            }

            private static bool OnCommandCompleted(IAsyncResult result)
            {
                var thisPtr = (InstanceCommandWithTemporaryHandleAsyncResult)result.AsyncState;
                thisPtr.commandResult = thisPtr.instanceStore.EndExecute(result);
                return true;
            }

            private static void OnComplete(AsyncResult result, Exception exception)
            {
                var thisPtr = (InstanceCommandWithTemporaryHandleAsyncResult)result;
                if (thisPtr.dependentTransaction != null)
                {
                    thisPtr.dependentTransaction.Complete();
                }
                thisPtr.temporaryHandle.Free();
            }
        }
    }
}