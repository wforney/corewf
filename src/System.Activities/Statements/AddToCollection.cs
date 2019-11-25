// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using Portable.Xaml.Markup;

    using System;
    using System.Activities.Internals;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;

    //[SuppressMessage(FxCop.Category.Naming, FxCop.Rule.IdentifiersShouldNotHaveIncorrectSuffix, Justification = "Optimizing for XAML naming.")]
    [ContentProperty("Collection")]
    public sealed class AddToCollection<T> : CodeActivity
    {
        [RequiredArgument]
        [DefaultValue(null)]
        public InArgument<ICollection<T>> Collection
        {
            get;
            set;
        }

        [RequiredArgument]
        [DefaultValue(null)]
        public InArgument<T> Item
        {
            get;
            set;
        }


        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            var arguments = new Collection<RuntimeArgument>();

            var collectionArgument = new RuntimeArgument("Collection", typeof(ICollection<T>), ArgumentDirection.In, true);
            metadata.Bind(this.Collection, collectionArgument);
            arguments.Add(collectionArgument);

            var itemArgument = new RuntimeArgument("Item", typeof(T), ArgumentDirection.In, true);
            metadata.Bind(this.Item, itemArgument);
            arguments.Add(itemArgument);

            metadata.SetArgumentsCollection(arguments);
        }

        protected override void Execute(CodeActivityContext context)
        {
            var collection = this.Collection.Get(context);
            if (collection == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CollectionActivityRequiresCollection(this.DisplayName)));
            }
            var item = this.Item.Get(context);

            collection.Add(item);
        }
    }
}
