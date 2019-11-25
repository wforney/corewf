// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Runtime.Serialization;

    public sealed partial class ActivityInstance
    {
        [DataContract]
        internal class ChildList : HybridCollection<ActivityInstance>
        {
            private static ReadOnlyCollection<ActivityInstance> emptyChildren;

            public ChildList()
                : base()
            {
            }

            public static ReadOnlyCollection<ActivityInstance> Empty
            {
                get
                {
                    if (emptyChildren == null)
                    {
                        emptyChildren = new ReadOnlyCollection<ActivityInstance>(Array.Empty<ActivityInstance>());
                    }

                    return emptyChildren;
                }
            }

            public void AppendChildren(ActivityUtilities.TreeProcessingList nextInstanceList, ref Queue<IList<ActivityInstance>> instancesRemaining)
            {
                // This is only called if there is at least one item in the list.

                if (base.SingleItem != null)
                {
                    nextInstanceList.Add(base.SingleItem);
                }
                else if (nextInstanceList.Count == 0)
                {
                    nextInstanceList.Set(base.MultipleItems);
                }
                else
                {
                    // Next instance list already has some stuff and we have multiple items. Let's
                    // enqueue them for later processing.

                    if (instancesRemaining == null)
                    {
                        instancesRemaining = new Queue<IList<ActivityInstance>>();
                    }

                    instancesRemaining.Enqueue(base.MultipleItems);
                }
            }

            public void FixupList(ActivityInstance parent, ActivityInstanceMap instanceMap, ActivityExecutor executor)
            {
                if (base.SingleItem != null)
                {
                    base.SingleItem.FixupInstance(parent, instanceMap, executor);
                }
                else
                {
                    for (var i = 0; i < base.MultipleItems.Count; i++)
                    {
                        base.MultipleItems[i].FixupInstance(parent, instanceMap, executor);
                    }
                }
            }
        }
    }
}
