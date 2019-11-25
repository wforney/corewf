// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    using System;
    using System.Activities.Runtime.DurableInstancing;
    using System.Collections.Generic;

    internal partial class ActivityExecutor
    {
        private class AssociateKeysAsyncResult : TransactedAsyncResult
        {
            private static readonly AsyncCompletion associatedCallback = new AsyncCompletion(OnAssociated);
            private readonly ActivityExecutor _executor;

            public AssociateKeysAsyncResult(ActivityExecutor executor, ICollection<InstanceKey> keysToAssociate, AsyncCallback callback, object state)
                : base(callback, state)
            {
                this._executor = executor;

                IAsyncResult? result;
                using (this.PrepareTransactionalCall(this._executor.CurrentTransaction))
                {
                    result = this._executor._host?.OnBeginAssociateKeys(keysToAssociate, this.PrepareAsyncCompletion(associatedCallback), this);
                }

                if (this.SyncContinue(result))
                {
                    this.Complete(true);
                }
            }

            private static bool OnAssociated(IAsyncResult result)
            {
                var thisPtr = (AssociateKeysAsyncResult)result.AsyncState;
                thisPtr._executor._host?.OnEndAssociateKeys(result);
                return true;
            }

            public static void End(IAsyncResult result)
            {
                AsyncResult.End<AssociateKeysAsyncResult>(result);
            }
        }
    }
}
