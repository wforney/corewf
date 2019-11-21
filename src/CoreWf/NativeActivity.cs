// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System;
    using System.Runtime.Serialization;

using System.Activities.DynamicUpdate;

    public abstract class NativeActivity : Activity
#if NET45
        , IInstanceUpdatable 
#endif
    {
        protected NativeActivity()
            : base()
        {
        }

        protected internal sealed override Version ImplementationVersion
        {
            get
            {
                return null;
            }
            set
            {
                if (value != null)
                {
                    throw FxTrace.Exception.AsError(new NotSupportedException());
                }
            }
        }

        [IgnoreDataMember]
        [Fx.Tag.KnownXamlExternal]
        public sealed override Func<Activity> Implementation
        {
            get
            {
                return null;
            }
            set
            {
                if (value != null)
                {
                    throw FxTrace.Exception.AsError(new NotSupportedException());
                }
            }
        }

        protected virtual bool CanInduceIdle
        {
            get
            {
                return false;
            }
        }

        internal override bool InternalCanInduceIdle
        {
            get
            {
                return this.CanInduceIdle;
            }
        }

        protected abstract void Execute(NativeActivityContext context);

        protected virtual void Abort(NativeActivityAbortContext context)
        {
        }

        protected virtual void Cancel(NativeActivityContext context)
        {
            if (!context.IsCancellationRequested)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.DefaultCancelationRequiresCancelHasBeenRequested));
            }
            context.Cancel();
        }

        sealed internal override void OnInternalCacheMetadata(bool createEmptyBindings)
        {
            var metadata = new NativeActivityMetadata(this, GetParentEnvironment(), createEmptyBindings);
            CacheMetadata(metadata);
            metadata.Dispose();
        }

        protected sealed override void CacheMetadata(ActivityMetadata metadata)
        {
            throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WrongCacheMetadataForNativeActivity));
        }

        protected virtual void CacheMetadata(NativeActivityMetadata metadata)
        {
            var information = new ReflectedInformation(this);

            // We bypass the metadata structure to avoid the checks for null entries
            SetArgumentsCollection(information.GetArguments(), metadata.CreateEmptyBindings);
            SetChildrenCollection(information.GetChildren());
            SetDelegatesCollection(information.GetDelegates());
            SetVariablesCollection(information.GetVariables());
        }

#if NET45
        internal sealed override void OnInternalCreateDynamicUpdateMap(DynamicUpdateMapBuilder.Finalizer finalizer,
            DynamicUpdateMapBuilder.IDefinitionMatcher matcher, Activity originalActivity)
        {
            NativeActivityUpdateMapMetadata metadata = new NativeActivityUpdateMapMetadata(finalizer, matcher, this);
            try
            {
                OnCreateDynamicUpdateMap(metadata, originalActivity);
            }
            finally
            {
                metadata.Dispose();
            }
        }

        protected sealed override void OnCreateDynamicUpdateMap(UpdateMapMetadata metadata, Activity originalActivity)
        {
            throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WrongOnCreateDynamicUpdateMapForNativeActivity));
        }

        [SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters,
            Justification = "Runtime passes in derived class to make more functionality availble to overriders")]
        protected virtual void OnCreateDynamicUpdateMap(NativeActivityUpdateMapMetadata metadata, Activity originalActivity)
        {
            // default UpdateMapMetadata.AllowUpdateInsideThisActivity is TRUE 
            if (!metadata.IsUpdateExplicitlyAllowedOrDisallowed && !DoPublicChildrenMatch(metadata, this, originalActivity))
            {
                metadata.DisallowUpdateInsideThisActivity(SR.PublicChildrenChangeBlockDU);
            }
        }

        internal static bool DoPublicChildrenMatch(UpdateMapMetadata metadata, Activity updatedActivity, Activity originalActivity)
        {
            return ActivityComparer.ListEquals(updatedActivity.Children, originalActivity.Children, metadata.AreMatch) &&
                ActivityComparer.ListEquals(updatedActivity.Delegates, originalActivity.Delegates, metadata.AreMatch) &&
                ActivityComparer.ListEquals(updatedActivity.ImportedChildren, originalActivity.ImportedChildren, metadata.AreMatch) &&
                ActivityComparer.ListEquals(updatedActivity.ImportedDelegates, originalActivity.ImportedDelegates, metadata.AreMatch);
        }

        void IInstanceUpdatable.InternalUpdateInstance(NativeActivityUpdateContext updateContext)
        {
            this.UpdateInstance(updateContext);
        }

        protected virtual void UpdateInstance(NativeActivityUpdateContext updateContext)
        {
            // note that this may be called multiple times on this same activity but with different instances
            // Override this only if you need to update runtime state as part of a dynamic update.            
        } 
#endif

        internal override void InternalExecute(ActivityInstance instance, ActivityExecutor executor, BookmarkManager bookmarkManager)
        {
            var context = executor.NativeActivityContextPool.Acquire();
            try
            {
                context.Initialize(instance, executor, bookmarkManager);
                Execute(context);
            }
            finally
            {
                context.Dispose();
                executor.NativeActivityContextPool.Release(context);
            }
        }

        internal override void InternalAbort(ActivityInstance instance, ActivityExecutor executor, Exception terminationReason)
        {
            var context = new NativeActivityAbortContext(instance, executor, terminationReason);
            try
            {
                Abort(context);
            }
            finally
            {
                context.Dispose();
            }
        }

        internal override void InternalCancel(ActivityInstance instance, ActivityExecutor executor, BookmarkManager bookmarkManager)
        {
            var context = executor.NativeActivityContextPool.Acquire();
            try
            {
                context.Initialize(instance, executor, bookmarkManager);
                Cancel(context);
            }
            finally
            {
                context.Dispose();
                executor.NativeActivityContextPool.Release(context);
            }
        }
    }

    public abstract class NativeActivity<TResult> : Activity<TResult>
#if NET45
        , IInstanceUpdatable 
#endif
    {

        protected NativeActivity()
            : base()
        {
        }

        protected internal sealed override Version ImplementationVersion
        {
            get
            {
                return null;
            }
            set
            {
                if (value != null)
                {
                    throw FxTrace.Exception.AsError(new NotSupportedException());
                }
            }
        }

        [IgnoreDataMember]
        [Fx.Tag.KnownXamlExternal]
        public sealed override Func<Activity> Implementation
        {
            get
            {
                return null;
            }
            set
            {
                if (value != null)
                {
                    throw FxTrace.Exception.AsError(new NotSupportedException());
                }
            }
        }

        protected virtual bool CanInduceIdle
        {
            get
            {
                return false;
            }
        }

        internal override bool InternalCanInduceIdle
        {
            get
            {
                return this.CanInduceIdle;
            }
        }

        protected abstract void Execute(NativeActivityContext context);

        protected virtual void Abort(NativeActivityAbortContext context)
        {
        }

        protected virtual void Cancel(NativeActivityContext context)
        {
            if (!context.IsCancellationRequested)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.DefaultCancelationRequiresCancelHasBeenRequested));
            }
            context.Cancel();
        }

        sealed internal override void OnInternalCacheMetadataExceptResult(bool createEmptyBindings)
        {
            var metadata = new NativeActivityMetadata(this, GetParentEnvironment(), createEmptyBindings);
            CacheMetadata(metadata);
            metadata.Dispose();
        }

        protected sealed override void CacheMetadata(ActivityMetadata metadata)
        {
            throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WrongCacheMetadataForNativeActivity));
        }

        protected virtual void CacheMetadata(NativeActivityMetadata metadata)
        {
            var information = new ReflectedInformation(this);

            // We bypass the metadata structure to avoid the checks for null entries
            SetArgumentsCollection(information.GetArguments(), metadata.CreateEmptyBindings);
            SetChildrenCollection(information.GetChildren());
            SetDelegatesCollection(information.GetDelegates());
            SetVariablesCollection(information.GetVariables());
        }

#if NET45
        internal sealed override void OnInternalCreateDynamicUpdateMap(DynamicUpdateMapBuilder.Finalizer finalizer,
            DynamicUpdateMapBuilder.IDefinitionMatcher matcher, Activity originalActivity)
        {
            NativeActivityUpdateMapMetadata metadata = new NativeActivityUpdateMapMetadata(finalizer, matcher, this);
            try
            {
                OnCreateDynamicUpdateMap(metadata, originalActivity);
            }
            finally
            {
                metadata.Dispose();
            }
        }

        protected sealed override void OnCreateDynamicUpdateMap(UpdateMapMetadata metadata, Activity originalActivity)
        {
            throw FxTrace.Exception.AsError(new InvalidOperationException(SR.WrongOnCreateDynamicUpdateMapForNativeActivity));
        }

        [SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters,
            Justification = "Runtime passes in derived class to make more functionality availble to overriders")]
        protected virtual void OnCreateDynamicUpdateMap(NativeActivityUpdateMapMetadata metadata, Activity originalActivity)
        {
            // default UpdateMapMetadata.AllowUpdateInsideThisActivity is TRUE 
            if (!metadata.IsUpdateExplicitlyAllowedOrDisallowed && !NativeActivity.DoPublicChildrenMatch(metadata, this, originalActivity))
            {
                metadata.DisallowUpdateInsideThisActivity(SR.PublicChildrenChangeBlockDU);
            }
        }

        void IInstanceUpdatable.InternalUpdateInstance(NativeActivityUpdateContext updateContext)
        {
            this.UpdateInstance(updateContext);
        }

        protected virtual void UpdateInstance(NativeActivityUpdateContext updateContext)
        {
            // note that this may be called multiple times on this same activity but with different instances
            // Override this only if you need to update runtime state as part of a dynamic update.            
        } 
#endif

        internal override void InternalExecute(ActivityInstance instance, ActivityExecutor executor, BookmarkManager bookmarkManager)
        {
            var context = executor.NativeActivityContextPool.Acquire();
            try
            {
                context.Initialize(instance, executor, bookmarkManager);
                Execute(context);
            }
            finally
            {
                context.Dispose();
                executor.NativeActivityContextPool.Release(context);
            }
        }

        internal override void InternalAbort(ActivityInstance instance, ActivityExecutor executor, Exception terminationReason)
        {
            var context = new NativeActivityAbortContext(instance, executor, terminationReason);
            try
            {
                Abort(context);
            }
            finally
            {
                context.Dispose();
            }
        }

        internal override void InternalCancel(ActivityInstance instance, ActivityExecutor executor, BookmarkManager bookmarkManager)
        {
            var context = executor.NativeActivityContextPool.Acquire();
            try
            {
                context.Initialize(instance, executor, bookmarkManager);
                Cancel(context);
            }
            finally
            {
                context.Dispose();
                executor.NativeActivityContextPool.Release(context);
            }
        }
    }  
}


