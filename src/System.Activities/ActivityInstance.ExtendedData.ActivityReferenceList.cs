// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Activities.Runtime;
    using System.Runtime.Serialization;

    public sealed partial class ActivityInstance
    {
        internal partial class ExtendedData
        {
            [DataContract]
            internal class ActivityReferenceList : HybridCollection<ActivityInstanceReference>
            {
                public ActivityReferenceList()
                    : base()
                {
                }

                public void FillInstanceMap(ActivityInstanceMap instanceMap)
                {
                    Fx.Assert(this.Count > 0, "Should only call this when we have items");

                    if (this.SingleItem != null)
                    {
                        instanceMap.AddEntry(this.SingleItem);
                    }
                    else
                    {
                        for (var i = 0; i < this.MultipleItems.Count; i++)
                        {
                            var reference = this.MultipleItems[i];

                            instanceMap.AddEntry(reference);
                        }
                    }
                }

                public void PurgeActivityReferences(ActivityInstanceMap instanceMap)
                {
                    Fx.Assert(this.Count > 0, "Should only call this when we have items");

                    if (this.SingleItem != null)
                    {
                        instanceMap.RemoveEntry(this.SingleItem);
                    }
                    else
                    {
                        for (var i = 0; i < this.MultipleItems.Count; i++)
                        {
                            instanceMap.RemoveEntry(this.MultipleItems[i]);
                        }
                    }
                }
            }
        }
    }
}
