// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using System;
    using System.Activities;
    using System.Collections.ObjectModel;
    using System.Activities.Internals;

    public sealed class DeleteBookmarkScope : NativeActivity
    {
        public DeleteBookmarkScope()
        {
        }

        public InArgument<BookmarkScope> Scope
        {
            get;
            set;
        }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            var subInstanceArgument = new RuntimeArgument("Scope", typeof(BookmarkScope), ArgumentDirection.In);
            metadata.Bind(this.Scope, subInstanceArgument);

            metadata.SetArgumentsCollection(new Collection<RuntimeArgument> { subInstanceArgument });
        }

        protected override void Execute(NativeActivityContext context)
        {
            var toUnregister = this.Scope.Get(context);

            if (toUnregister == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotUnregisterNullBookmarkScope));
            }

            if (toUnregister.Equals(context.DefaultBookmarkScope))
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotUnregisterDefaultBookmarkScope));
            }

            context.UnregisterBookmarkScope(toUnregister);
        }
    }
}
