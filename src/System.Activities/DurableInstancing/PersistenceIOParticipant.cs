// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Persistence
{
    using System;
    using System.Collections.Generic;
    using System.Transactions;
    using System.Xml.Linq;
    using System.Activities.Runtime;

    /// <summary>
    /// The PersistenceIOParticipant class.
    /// Implements the <see cref="PersistenceParticipant" />
    /// </summary>
    /// <seealso cref="PersistenceParticipant" />
    public abstract class PersistenceIOParticipant : PersistenceParticipant
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PersistenceIOParticipant"/> class.
        /// </summary>
        /// <param name="isSaveTransactionRequired">if set to <c>true</c> [is save transaction required].</param>
        /// <param name="isLoadTransactionRequired">if set to <c>true</c> [is load transaction required].</param>
        protected PersistenceIOParticipant(bool isSaveTransactionRequired, bool isLoadTransactionRequired)
            : base(isSaveTransactionRequired, isLoadTransactionRequired)
        {
        }

        /// <summary>
        /// Begins the on save.
        /// </summary>
        /// <param name="readWriteValues">The read write values.</param>
        /// <param name="writeOnlyValues">The write only values.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        /// <remarks>Passed-in dictionaries are read-only.</remarks>
        [Fx.Tag.Throws.Timeout("The operation could not be completed before the timeout.  The transaction should be rolled back and the pipeline aborted.")]
        [Fx.Tag.Throws(typeof(OperationCanceledException), "The operation has been aborted.  The transaction should be rolled back and the pipeline aborted.")]
        [Fx.Tag.Throws(typeof(TransactionException), "The transaction associated with the operation has failed.  The pipeline should be aborted.")]
        protected virtual IAsyncResult BeginOnSave(IDictionary<XName, object> readWriteValues, IDictionary<XName, object> writeOnlyValues, TimeSpan timeout, AsyncCallback callback, object state) => new CompletedAsyncResult(callback, state);

        /// <summary>
        /// Ends the on save.
        /// </summary>
        /// <param name="result">The result.</param>
        [Fx.Tag.InheritThrows(From = "BeginOnSave")]
        protected virtual void EndOnSave(IAsyncResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            CompletedAsyncResult.End(result);
        }

        /// <summary>
        /// Begins the on load.
        /// </summary>
        /// <param name="readWriteValues">The read write values.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        /// <remarks>Passed-in dictionary is read-only.</remarks>
        [Fx.Tag.InheritThrows(From = "BeginOnSave")]
        protected virtual IAsyncResult BeginOnLoad(IDictionary<XName, object> readWriteValues, TimeSpan timeout, AsyncCallback callback, object state) => new CompletedAsyncResult(callback, state);

        /// <summary>
        /// Ends the on load.
        /// </summary>
        /// <param name="result">The result.</param>
        [Fx.Tag.InheritThrows(From = "BeginOnLoad")]
        protected virtual void EndOnLoad(IAsyncResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            CompletedAsyncResult.End(result);
        }

        /// <summary>
        /// Aborts this instance.
        /// </summary>
        protected abstract void Abort();

        /// <summary>
        /// Internals the begin on save.
        /// </summary>
        /// <param name="readWriteValues">The read write values.</param>
        /// <param name="writeOnlyValues">The write only values.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        internal override IAsyncResult InternalBeginOnSave(IDictionary<XName, object> readWriteValues, IDictionary<XName, object> writeOnlyValues, TimeSpan timeout, AsyncCallback callback, object state) => this.BeginOnSave(readWriteValues, writeOnlyValues, timeout, callback, state);

        /// <summary>
        /// Internals the end on save.
        /// </summary>
        /// <param name="result">The result.</param>
        internal override void InternalEndOnSave(IAsyncResult result) => this.EndOnSave(result);

        /// <summary>
        /// Internals the begin on load.
        /// </summary>
        /// <param name="readWriteValues">The read write values.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        internal override IAsyncResult InternalBeginOnLoad(IDictionary<XName, object> readWriteValues, TimeSpan timeout, AsyncCallback callback, object state) => this.BeginOnLoad(readWriteValues, timeout, callback, state);

        /// <summary>
        /// Internals the end on load.
        /// </summary>
        /// <param name="result">The result.</param>
        internal override void InternalEndOnLoad(IAsyncResult result) => this.EndOnLoad(result);

        /// <summary>
        /// Internals the abort.
        /// </summary>
        internal override void InternalAbort() => this.Abort();
    }
}
