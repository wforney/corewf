// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Activities.Runtime;
    using System.Collections.Generic;

    internal static partial class ActivityUtilities
    {
        public class TreeProcessingList
        {
            private bool addRequiresNewList;
            private IList<ActivityInstance>? multipleItems;
            private ActivityInstance? singleItem;

            public TreeProcessingList()
            {
            }

            public int Count
            {
                get
                {
                    if (this.singleItem != null)
                    {
                        return 1;
                    }

                    if (this.multipleItems != null)
                    {
                        return this.multipleItems.Count;
                    }

                    return 0;
                }
            }

            public ActivityInstance this[int index]
            {
                get
                {
                    if (this.singleItem != null)
                    {
                        Fx.Assert(index == 0, "We expect users of TreeProcessingList never to be out of range.");
                        return this.singleItem;
                    }
                    else
                    {
                        Fx.Assert(this.multipleItems != null, "Users shouldn't call this if we have no items.");
                        Fx.Assert(this.multipleItems.Count > index, "Users should never be out of range.");

                        return this.multipleItems[index];
                    }
                }
            }

            public void Add(ActivityInstance item)
            {
                if (this.multipleItems != null)
                {
                    if (this.addRequiresNewList)
                    {
                        this.multipleItems = new List<ActivityInstance>(this.multipleItems);
                        this.addRequiresNewList = false;
                    }

                    this.multipleItems.Add(item);
                }
                else if (this.singleItem != null)
                {
                    this.multipleItems = new List<ActivityInstance>(2);
                    this.multipleItems.Add(this.singleItem);
                    this.multipleItems.Add(item);
                    this.singleItem = null;
                }
                else
                {
                    this.singleItem = item;
                }
            }

            // Because of how we use this we don't need a Clear(). Basically we gain nothing by
            // clearing the multipleItems list and hanging onto it.
            public void Reset()
            {
                this.addRequiresNewList = false;
                this.multipleItems = null;
                this.singleItem = null;
            }

            public void Set(IList<ActivityInstance>? listToSet)
            {
                Fx.Assert(this.singleItem == null && (this.multipleItems == null || this.multipleItems.Count == 0), "We should not have any items if calling set.");

                this.multipleItems = listToSet;
                this.addRequiresNewList = true;
            }

            public void TransferTo(TreeProcessingList otherList)
            {
                otherList.singleItem = this.singleItem;
                otherList.multipleItems = this.multipleItems;
                otherList.addRequiresNewList = this.addRequiresNewList;

                this.Reset();
            }
        }
    }
}
