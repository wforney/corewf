// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;
    using System.Activities.Internals;

    /// <summary>
    /// The HandleInitializationContext class. This class cannot be inherited.
    /// </summary>
    [Fx.Tag.XamlVisible(false)]
    public sealed class HandleInitializationContext 
    {
        private bool isDiposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="HandleInitializationContext"/> class.
        /// </summary>
        /// <param name="executor">The executor.</param>
        /// <param name="scope">The scope.</param>
        internal HandleInitializationContext(ActivityExecutor? executor, ActivityInstance scope)
        {
            this.Executor = executor;
            this.OwningActivityInstance = scope;
        }

        /// <summary>
        /// Gets the owning activity instance.
        /// </summary>
        /// <value>The owning activity instance.</value>
        internal ActivityInstance OwningActivityInstance { get; }

        /// <summary>
        /// Gets the executor.
        /// </summary>
        /// <value>The executor.</value>
        internal ActivityExecutor? Executor { get; }

        /// <summary>
        /// Creates the and initialize handle.
        /// </summary>
        /// <typeparam name="THandle">The type of the t handle.</typeparam>
        /// <returns>THandle.</returns>
        public THandle CreateAndInitializeHandle<THandle>() where THandle : Handle
        {
            this.ThrowIfDisposed();
            var value = Activator.CreateInstance<THandle>();

            value.Initialize(this);

            // If we have a scope, we need to add this new handle to the LocationEnvironment.
            if (this.OwningActivityInstance != null)
            {
                this.OwningActivityInstance.Environment.AddHandle(value);
            }
            // otherwise add it to the Executor.
            else
            {
                this.Executor?.AddHandle(value);
            }

            return value;
        }

        /// <summary>
        /// Gets the extension.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>T.</returns>
        public T? GetExtension<T>() where T : class => this.Executor?.GetExtension<T>();

        /// <summary>
        /// Uninitializes the handle.
        /// </summary>
        /// <param name="handle">The handle.</param>
        public void UninitializeHandle(Handle handle)
        {
            this.ThrowIfDisposed();
            handle?.Uninitialize(this);
        }

        internal object CreateAndInitializeHandle(Type handleType)
        {
            Fx.Assert(ActivityUtilities.IsHandle(handleType), "This should only be called with Handle subtypes.");

            var value = Activator.CreateInstance(handleType);

            ((Handle)value).Initialize(this);

            // If we have a scope, we need to add this new handle to the LocationEnvironment.
            if (this.OwningActivityInstance != null)
            {
                this.OwningActivityInstance.Environment.AddHandle((Handle)value);
            }
            // otherwise add it to the Executor.
            else
            {
                this.Executor?.AddHandle((Handle)value);
            }

            return value;
        }

        internal BookmarkScope? CreateAndRegisterBookmarkScope() =>
            this.Executor?.BookmarkScopeManager.CreateAndRegisterScope(Guid.Empty);

        internal void UnregisterBookmarkScope(BookmarkScope bookmarkScope)
        {
            Fx.Assert(bookmarkScope != null, "The sub instance should not equal null.");
            if (bookmarkScope == null)
            {
                throw new ArgumentNullException(nameof(bookmarkScope));
            }

            this.Executor?.BookmarkScopeManager.UnregisterScope(bookmarkScope);
        }

        private void ThrowIfDisposed()
        {
            if (this.isDiposed)
            {
                throw FxTrace.Exception.AsError(new ObjectDisposedException(SR.HandleInitializationContextDisposed));
            }
        }

        internal void Dispose() => this.isDiposed = true;
    }
}


