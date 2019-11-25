// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    using System.Threading;

    /// <summary>
    /// The AsyncResult class. Implements the <see cref="System.IAsyncResult" />
    /// </summary>
    /// <seealso cref="System.IAsyncResult" />
    /// <remarks>AsyncResult starts acquired; Complete releases.</remarks>
    [Fx.Tag.SynchronizationPrimitive(Fx.Tag.BlocksUsing.ManualResetEvent, SupportsAsync = true, ReleaseMethod = "Complete")]
    internal abstract class AsyncResult : IAsyncResult, IDisposable
    {
        private static AsyncCallback? s_asyncCompletionWrapperCallback;
        private readonly AsyncCallback _callback;
        private bool _endCalled;
        private Exception? _exception;
        private AsyncCompletion? _nextAsyncCompletion;
        private Action? _beforePrepareAsyncCompletionAction;
        private Func<IAsyncResult, bool>? _checkSyncValidationFunc;

        [Fx.Tag.SynchronizationObject]
        private ManualResetEvent? _manualResetEvent;

        [Fx.Tag.SynchronizationObject(Blocking = false)]
        private readonly object _thisLock;

        //#if DEBUG
        //        StackTrace endStack;
        //        StackTrace completeStack;
        //        UncompletedAsyncResultMarker marker;
        //#endif

        protected AsyncResult(AsyncCallback callback, object state)
        {
            this._callback = callback;
            this.AsyncState = state;
            this._thisLock = new object();

            //#if DEBUG
            //            this.marker = new UncompletedAsyncResultMarker(this);
            //#endif
        }

        public object AsyncState { get; }

        public WaitHandle AsyncWaitHandle
        {
            get
            {
                if (this._manualResetEvent != null)
                {
                    return this._manualResetEvent;
                }

                lock (this.ThisLock)
                {
                    if (this._manualResetEvent == null)
                    {
                        this._manualResetEvent = new ManualResetEvent(this.IsCompleted);
                    }
                }

                return this._manualResetEvent;
            }
        }

        public bool CompletedSynchronously { get; private set; }

        public bool HasCallback => this._callback != null;

        public bool IsCompleted { get; private set; }

        /// <summary>
        /// Gets or sets the on completing.
        /// </summary>
        /// <value>The on completing.</value>
        /// <remarks>used in conjunction with PrepareAsyncCompletion to allow for finally blocks</remarks>
        protected Action<AsyncResult, Exception?>? OnCompleting { get; set; }

        private object ThisLock => this._thisLock;

        /// <summary>
        /// Gets or sets the virtual callback.
        /// </summary>
        /// <value>The virtual callback.</value>
        /// <remarks>
        /// subclasses like TraceAsyncResult can use this to wrap the callback functionality in a scope
        /// </remarks>
        protected Action<AsyncCallback, IAsyncResult>? VirtualCallback { get; set; }

        protected void Complete(bool completedSynchronously)
        {
            if (this.IsCompleted)
            {
                throw Fx.Exception.AsError(new InvalidOperationException(SR.AsyncResultCompletedTwice(this.GetType())));
            }

            //#if DEBUG
            //            this.marker.AsyncResult = null;
            //            this.marker = null;
            //            if (!Fx.FastDebug && completeStack == null)
            //            {
            //                completeStack = new StackTrace();
            //            }
            //#endif

            this.CompletedSynchronously = completedSynchronously;
            if (this.OnCompleting != null)
            {
                // Allow exception replacement, like a catch/throw pattern.
                try
                {
                    this.OnCompleting(this, this._exception);
                }
                catch (Exception exception)
                {
                    if (Fx.IsFatal(exception))
                    {
                        throw;
                    }
                    this._exception = exception;
                }
            }

            if (completedSynchronously)
            {
                // If we completedSynchronously, then there's no chance that the manualResetEvent
                // was created so we don't need to worry about a race condition
                Fx.Assert(this._manualResetEvent == null, "No ManualResetEvent should be created for a synchronous AsyncResult.");
                this.IsCompleted = true;
            }
            else
            {
                lock (this.ThisLock)
                {
                    this.IsCompleted = true;
                    if (this._manualResetEvent != null)
                    {
                        this._manualResetEvent.Set();
                    }
                }
            }

            if (this._callback != null)
            {
                try
                {
                    if (this.VirtualCallback != null)
                    {
                        this.VirtualCallback(this._callback, this);
                    }
                    else
                    {
                        this._callback(this);
                    }
                }
#pragma warning disable 1634
#pragma warning suppress 56500 // transferring exception to another thread
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                    {
                        throw;
                    }

                    throw Fx.Exception.AsError(new CallbackException(SR.AsyncCallbackThrewException, e));
                }
#pragma warning restore 1634
            }
        }

        protected void Complete(bool completedSynchronously, Exception? exception)
        {
            this._exception = exception;
            this.Complete(completedSynchronously);
        }

        private static void AsyncCompletionWrapperCallback(IAsyncResult result)
        {
            if (result == null)
            {
                throw Fx.Exception.AsError(new InvalidOperationException(SR.InvalidNullAsyncResult));
            }
            if (result.CompletedSynchronously)
            {
                return;
            }

            var thisPtr = (AsyncResult)result.AsyncState;
            if (!thisPtr.OnContinueAsyncCompletion(result))
            {
                return;
            }

            var callback = thisPtr.GetNextCompletion();
            if (callback == null)
            {
                ThrowInvalidAsyncResult(result);
            }

            var completeSelf = false;
            Exception? completionException = null;
            try
            {
                completeSelf = callback?.Invoke(result) ?? false;
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }
                completeSelf = true;
                completionException = e;
            }

            if (completeSelf)
            {
                thisPtr.Complete(false, completionException);
            }
        }

        /// <summary>
        /// Called when [continue asynchronous completion].
        /// </summary>
        /// <param name="result">The result.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        /// <remarks>
        /// Note: this should be only derived by the TransactedAsyncResult        
        /// </remarks>
        protected virtual bool OnContinueAsyncCompletion(IAsyncResult result) => true;

        /// <summary>
        /// Sets the before prepare asynchronous completion action.
        /// </summary>
        /// <param name="beforePrepareAsyncCompletionAction">The before prepare asynchronous completion action.</param>
        /// <remarks>
        /// Note: this should be used only by the TransactedAsyncResult        
        /// </remarks>
        protected void SetBeforePrepareAsyncCompletionAction(Action beforePrepareAsyncCompletionAction) => this._beforePrepareAsyncCompletionAction = beforePrepareAsyncCompletionAction;

        /// <summary>
        /// Sets the check synchronize validation function.
        /// </summary>
        /// <param name="checkSyncValidationFunc">The check synchronize validation function.</param>
        /// <remarks>
        /// Note: this should be used only by the TransactedAsyncResult        
        /// </remarks>
        protected void SetCheckSyncValidationFunc(Func<IAsyncResult, bool> checkSyncValidationFunc) => this._checkSyncValidationFunc = checkSyncValidationFunc;

        protected AsyncCallback PrepareAsyncCompletion(AsyncCompletion callback)
        {
            this._beforePrepareAsyncCompletionAction?.Invoke();

            this._nextAsyncCompletion = callback;
            if (AsyncResult.s_asyncCompletionWrapperCallback == null)
            {
                AsyncResult.s_asyncCompletionWrapperCallback = Fx.ThunkCallback(new AsyncCallback(AsyncCompletionWrapperCallback));
            }

            return AsyncResult.s_asyncCompletionWrapperCallback;
        }

        protected bool CheckSyncContinue(IAsyncResult result) => this.TryContinueHelper(result, out var dummy);

        protected bool SyncContinue(IAsyncResult? result) =>
            this.TryContinueHelper(result, out var callback) ? callback?.Invoke(result) ?? false : false;

        private bool TryContinueHelper(IAsyncResult? result, out AsyncCompletion? callback)
        {
            if (result == null)
            {
                throw Fx.Exception.AsError(new InvalidOperationException(SR.InvalidNullAsyncResult));
            }

            callback = null;
            if (this._checkSyncValidationFunc != null)
            {
                if (!this._checkSyncValidationFunc(result))
                {
                    return false;
                }
            }
            else if (!result.CompletedSynchronously)
            {
                return false;
            }

            callback = this.GetNextCompletion();
            if (callback == null)
            {
                ThrowInvalidAsyncResult("Only call Check/SyncContinue once per async operation (once per PrepareAsyncCompletion).");
            }

            return true;
        }

        private AsyncCompletion? GetNextCompletion()
        {
            var result = this._nextAsyncCompletion;
            this._nextAsyncCompletion = null;
            return result;
        }

        /// <summary>
        /// Throws the invalid asynchronous result.
        /// </summary>
        /// <param name="result">The result.</param>
        /// <exception cref="InvalidOperationException"></exception>
        protected static void ThrowInvalidAsyncResult(IAsyncResult result) =>
            throw Fx.Exception.AsError(new InvalidOperationException(SR.InvalidAsyncResultImplementation(result.GetType())));

        protected static void ThrowInvalidAsyncResult(string debugText)
        {
            var message = SR.InvalidAsyncResultImplementationGeneric;
            if (debugText != null)
            {
#if DEBUG
                message += $" {debugText}";
#endif
            }
            throw Fx.Exception.AsError(new InvalidOperationException(message));
        }

        [Fx.Tag.Blocking(Conditional = "!asyncResult.isCompleted")]
        public static TAsyncResult End<TAsyncResult>(IAsyncResult result)
            where TAsyncResult : AsyncResult
        {
            if (result == null)
            {
                throw Fx.Exception.ArgumentNull(nameof(result));
            }

            if (!(result is TAsyncResult asyncResult))
            {
                throw Fx.Exception.Argument(nameof(result), SR.InvalidAsyncResult);
            }

            if (asyncResult._endCalled)
            {
                throw Fx.Exception.AsError(new InvalidOperationException(SR.AsyncResultAlreadyEnded));
            }

            //#if DEBUG
            //            if (!Fx.FastDebug && asyncResult.endStack == null)
            //            {
            //                asyncResult.endStack = new StackTrace();
            //            }
            //#endif

            asyncResult._endCalled = true;

            if (!asyncResult.IsCompleted)
            {
                asyncResult.AsyncWaitHandle.WaitOne();
            }

            if (asyncResult._manualResetEvent != null)
            {
                //asyncResult.manualResetEvent.Close();
                asyncResult._manualResetEvent.Dispose();
            }

            if (asyncResult._exception != null)
            {
                throw Fx.Exception.AsError(asyncResult._exception);
            }

            return asyncResult;
        }

        // can be utilized by subclasses to write core completion code for both the sync and async
        // paths in one location, signalling chainable synchronous completion with the boolean
        // result, and leveraging PrepareAsyncCompletion for conversion to an AsyncCallback.
        // NOTE: requires that "this" is passed in as the state object to the asynchronous sub-call
        // being used with a completion routine.
        protected delegate bool AsyncCompletion(IAsyncResult? result);

#if DEBUG

        private class UncompletedAsyncResultMarker
        {
            public UncompletedAsyncResultMarker(AsyncResult result) => this.AsyncResult = result;

            //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode,
            //Justification = "Debug-only facility")]
            public AsyncResult AsyncResult { get; set; }
        }

#endif

        /// <summary>
        /// To detect redundant calls
        /// </summary>
        private bool disposedValue = false;

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> to release both managed and unmanaged resources; <c>false</c> to release
        /// only unmanaged resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects).
                    this._manualResetEvent?.Dispose();
                }

                // free unmanaged resources (unmanaged objects) and override a finalizer below. set
                // large fields to null.

                this.disposedValue = true;
            }
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="AsyncResult" /> class.
        /// </summary>
        ~AsyncResult()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            this.Dispose(false);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting
        /// unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
