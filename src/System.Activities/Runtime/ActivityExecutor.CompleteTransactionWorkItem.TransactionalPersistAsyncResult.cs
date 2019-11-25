// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    using System;
    using System.Activities.Runtime.DurableInstancing;

    internal partial class ActivityExecutor
    {

        private partial class CompleteTransactionWorkItem
        {
            private class TransactionalPersistAsyncResult : TransactedAsyncResult
            {
                private readonly CompleteTransactionWorkItem _workItem;
                private static readonly AsyncCompletion onPersistComplete = new AsyncCompletion(OnPersistComplete);
                private readonly ActivityExecutor _executor;

                public TransactionalPersistAsyncResult(ActivityExecutor executor, AsyncCallback callback, object state)
                    : base(callback, state)
                {
                    this._executor = executor;
                    this._workItem = (CompleteTransactionWorkItem)state;
                    IAsyncResult? result = null;
                    using (this.PrepareTransactionalCall(this._executor.CurrentTransaction))
                    {
                        try
                        {
                            result = this._executor._host?.OnBeginPersist(this.PrepareAsyncCompletion(onPersistComplete), this);
                        }
                        catch (Exception e)
                        {
                            if (Fx.IsFatal(e))
                            {
                                throw;
                            }
                            this._workItem._workflowAbortException = e;
                            throw;
                        }
                    }
                    if (this.SyncContinue(result))
                    {
                        this.Complete(true);
                    }
                }

                public static void End(IAsyncResult result) => End<TransactionalPersistAsyncResult>(result);

                private static bool OnPersistComplete(IAsyncResult result)
                {
                    var thisPtr = (TransactionalPersistAsyncResult)result.AsyncState;

                    try
                    {
                        thisPtr._executor._host?.OnEndPersist(result);
                    }
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                        {
                            throw;
                        }
                        thisPtr._workItem._workflowAbortException = e;
                        throw;
                    }

                    return true;
                }
            }
        }
    }
}
