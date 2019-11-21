// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
#if NET45
    using System.Activities.Debugger;
#endif

    using System.Activities.Runtime;
    using System.Collections.ObjectModel;
    using System.Xaml;
    using System;

    public sealed partial class ActivityBuilder
#if NET45
        : IDebuggableWorkflowTree
#endif
    {
        // See back-compat requirements in comment above. Design is:
        // - First value added to collection when it is empty becomes the single PropertyReference value
        // - If the single value is removed, then PropertyReference AP is removed
        // - If PropertyReference AP is set to null, we remove the single value.
        // - If PropertyReference is set to non-null, we replace the existing single value if there
        //    is one, or else add the new value to the collection.
        /// <summary>
        /// The PropertyReferenceCollection class.
        /// Implements the <see cref="System.Collections.ObjectModel.Collection{System.Activities.ActivityPropertyReference}" />
        /// </summary>
        /// <seealso cref="System.Collections.ObjectModel.Collection{System.Activities.ActivityPropertyReference}" />
        private class PropertyReferenceCollection : Collection<ActivityPropertyReference>
        {
            /// <summary>
            /// The target object
            /// </summary>
            private readonly WeakReference targetObject;

            /// <summary>
            /// The single item index
            /// </summary>
            private int singleItemIndex = -1;

            /// <summary>
            /// Initializes a new instance of the <see cref="PropertyReferenceCollection"/> class.
            /// </summary>
            /// <param name="target">The target.</param>
            public PropertyReferenceCollection(object target) => this.targetObject = new WeakReference(target);

            /// <summary>
            /// Gets or sets the single item.
            /// </summary>
            /// <value>The single item.</value>
            public ActivityPropertyReference SingleItem
            {
                get => this.singleItemIndex >= 0 ? this[this.singleItemIndex] : null;
                set
                {
                    if (this.singleItemIndex >= 0)
                    {
                        if (value != null)
                        {
                            this.SetItem(this.singleItemIndex, value);
                        }
                        else
                        {
                            this.RemoveItem(this.singleItemIndex);
                        }
                    }
                    else if (value != null)
                    {
                        this.Add(value);
                        if (this.Count > 1)
                        {
                            this.singleItemIndex = this.Count - 1;
                            this.UpdateAttachedProperty();
                        }
                    }
                }
            }

            /// <summary>
            /// Removes all elements from the <see cref="Collection{T}" />.
            /// </summary>
            protected override void ClearItems()
            {
                this.singleItemIndex = -1;
                this.UpdateAttachedProperty();
            }

            /// <summary>
            /// Inserts an element into the <see cref="Collection{T}" /> at the specified index.
            /// </summary>
            /// <param name="index">The zero-based index at which <paramref name="item" /> should be inserted.</param>
            /// <param name="item">The object to insert. The value can be <see langword="null" /> for reference types.</param>
            protected override void InsertItem(int index, ActivityPropertyReference item)
            {
                base.InsertItem(index, item);
                if (index <= this.singleItemIndex)
                {
                    this.singleItemIndex++;
                }
                else if (this.Count == 1)
                {
                    Fx.Assert(this.singleItemIndex < 0, "How did we have an index if we were empty?");
                    this.singleItemIndex = 0;
                    this.UpdateAttachedProperty();
                }
            }

            /// <summary>
            /// Removes the element at the specified index of the <see cref="Collection{T}" />.
            /// </summary>
            /// <param name="index">The zero-based index of the element to remove.</param>
            protected override void RemoveItem(int index)
            {
                base.RemoveItem(index);
                if (index < this.singleItemIndex)
                {
                    this.singleItemIndex--;
                }
                else if (index == this.singleItemIndex)
                {
                    this.singleItemIndex = -1;
                    this.UpdateAttachedProperty();
                }
            }

            /// <summary>
            /// Replaces the element at the specified index.
            /// </summary>
            /// <param name="index">The zero-based index of the element to replace.</param>
            /// <param name="item">The new value for the element at the specified index. The value can be <see langword="null" /> for reference types.</param>
            protected override void SetItem(int index, ActivityPropertyReference item)
            {
                base.SetItem(index, item);
                if (index == this.singleItemIndex)
                {
                    this.UpdateAttachedProperty();
                }
            }

            /// <summary>
            /// Updates the attached property.
            /// </summary>
            private void UpdateAttachedProperty()
            {
                var target = this.targetObject.Target;
                if (target != null)
                {
                    if (this.singleItemIndex >= 0)
                    {
                        AttachablePropertyServices.SetProperty(target, propertyReferencePropertyID, this[this.singleItemIndex]);
                    }
                    else
                    {
                        AttachablePropertyServices.RemoveProperty(target, propertyReferencePropertyID);
                    }
                }
            }
        }
    }
}