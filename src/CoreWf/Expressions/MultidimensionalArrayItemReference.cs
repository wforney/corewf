// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{
    using System;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.Collections;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Runtime.Serialization;
    using System.Windows.Markup;

    /// <summary>
    /// The MultidimensionalArrayItemReference class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.CodeActivity{System.Activities.Location{TItem}}" />
    /// </summary>
    /// <typeparam name="TItem">The type of the t item.</typeparam>
    /// <seealso cref="System.Activities.CodeActivity{System.Activities.Location{TItem}}" />
    [ContentProperty("Indices")]
    public sealed class MultidimensionalArrayItemReference<TItem> : CodeActivity<Location<TItem>>
    {
        /// <summary>
        /// The indices
        /// </summary>
        private Collection<InArgument<int>> indices;

        /// <summary>
        /// Gets or sets the array.
        /// </summary>
        /// <value>The array.</value>
        [RequiredArgument]
        [DefaultValue(null)]
        public InArgument<Array> Array { get; set; }

        /// <summary>
        /// Gets the indices.
        /// </summary>
        /// <value>The indices.</value>
        [DefaultValue(null)]
        [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations", Justification = "<Pending>")]
        public Collection<InArgument<int>> Indices
        {
            get
            {
                if (this.indices == null)
                {
                    this.indices = new ValidatingCollection<InArgument<int>>
                    {
                        // disallow null values
                        OnAddValidationCallback = item =>
                        {
                            if (item == null)
                            {
                                throw FxTrace.Exception.ArgumentNull(nameof(item));
                            }
                        },
                    };
                }
         
                return this.indices;
            }
        }

        /// <summary>
        /// Caches the metadata.
        /// </summary>
        /// <param name="metadata">The metadata.</param>
        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            if (this.Indices.Count == 0)
            {
                metadata.AddValidationError(SR.IndicesAreNeeded(this.GetType().Name, this.DisplayName));
            }

            var arrayArgument = new RuntimeArgument("Array", typeof(Array), ArgumentDirection.In, true);
            metadata.Bind(this.Array, arrayArgument);
            metadata.AddArgument(arrayArgument);

            for (var i = 0; i < this.Indices.Count; i++)
            {
                var indexArgument = new RuntimeArgument("Index_" + i, typeof(int), ArgumentDirection.In, true);
                metadata.Bind(this.Indices[i], indexArgument);
                metadata.AddArgument(indexArgument);
            }

            var resultArgument = new RuntimeArgument("Result", typeof(Location<TItem>), ArgumentDirection.Out);
            metadata.Bind(this.Result, resultArgument);
            metadata.AddArgument(resultArgument);
        }

        /// <summary>
        /// Executes the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>Location&lt;TItem&gt;.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="InvalidCastException"></exception>
        protected override Location<TItem> Execute(CodeActivityContext context)
        {
            var items = this.Array.Get(context);

            if (items == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.MemberCannotBeNull("Array", this.GetType().Name, this.DisplayName)));
            }

            var realItemType = items.GetType().GetElementType();
            if (!TypeHelper.AreTypesCompatible(typeof(TItem), realItemType))
            {
                throw FxTrace.Exception.AsError(new InvalidCastException(SR.IncompatibleTypeForMultidimensionalArrayItemReference(typeof(TItem).Name, realItemType.Name)));
            }

            var itemIndex = new int[this.Indices.Count];
            for (var i = 0; i < this.Indices.Count; i++)
            {
                itemIndex[i] = this.Indices[i].Get(context);
            }

            return new MultidimensionArrayLocation(items, itemIndex);
        }

        /// <summary>
        /// The MultidimensionArrayLocation class.
        /// Implements the <see cref="System.Activities.Location{TItem}" />
        /// </summary>
        /// <seealso cref="System.Activities.Location{TItem}" />
        [DataContract]
        internal class MultidimensionArrayLocation : Location<TItem>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MultidimensionArrayLocation"/> class.
            /// </summary>
            /// <param name="array">The array.</param>
            /// <param name="indices">The indices.</param>
            public MultidimensionArrayLocation(Array array, int[] indices)
                : base()
            {
                this.Serializedarray = array;
                this.SerializedIndices = indices;
            }

            /// <summary>
            /// Gets or sets the value.
            /// </summary>
            /// <value>The value.</value>
            public override TItem Value
            {
                get => (TItem)this.Serializedarray.GetValue(this.SerializedIndices);
                set => this.Serializedarray.SetValue(value, this.SerializedIndices);
            }

            /// <summary>
            /// Gets or sets the serializedarray.
            /// </summary>
            /// <value>The serializedarray.</value>
            [DataMember(Name = "array")]
            internal Array Serializedarray { get; set; }

            /// <summary>
            /// Gets or sets the serialized indices.
            /// </summary>
            /// <value>The serialized indices.</value>
            [DataMember(EmitDefaultValue = false, Name = "indices")]
            internal int[] SerializedIndices { get; set; }
        }
    }
}
