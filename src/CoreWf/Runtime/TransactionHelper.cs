// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.
namespace System.Activities.Runtime
{
    using System.Transactions;

    internal static class TransactionHelper
    {
        public static void ThrowIfTransactionAbortedOrInDoubt(Transaction transaction)
        {
            if (transaction == null)
            {
                return;
            }

            if (transaction.TransactionInformation.Status == TransactionStatus.Aborted || transaction.TransactionInformation.Status == TransactionStatus.InDoubt)
            {
                //This will throw TransactionAbortedException/TransactionInDoubtException with corresponding inner exception if any
                using (var scope = new TransactionScope(transaction))
                {
                    //empty
                }
            }
        }

        // If the transaction has aborted then we switch over to a new transaction
        // which we will immediately abort after setting Transaction.Current
        public static TransactionScope CreateTransactionScope(Transaction transaction)
        {
            try
            {
                return transaction == null ? null : new TransactionScope(transaction);
            }
            catch (TransactionAbortedException)
            {
                var tempTransaction = new CommittableTransaction();
                try
                {
                    return new TransactionScope(tempTransaction.Clone());
                }
                finally
                {
                    tempTransaction.Rollback();
                }
            }
        }

        public static void CompleteTransactionScope(ref TransactionScope scope)
        {
            var localScope = scope;
            if (localScope != null)
            {
                scope = null;
                try
                {
                    localScope.Complete();
                }
                finally
                {
                    localScope.Dispose();
                }
            }
        }
    }
}
