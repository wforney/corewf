// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System;
    using System.Collections.Generic;

    internal class IdSpace
    {
        private int lastId;
        private IList<Activity> members;

        public IdSpace()
        {
        }

        public IdSpace(IdSpace parent, int parentId)
        {
            this.Parent = parent;
            this.ParentId = parentId;
        }

        public IdSpace Parent
        {
            get;
            private set;
        }

        public int ParentId
        {
            get;
            private set;
        }

        public int MemberCount
        {
            get
            {
                if (this.members == null)
                {
                    return 0;
                }
                else
                {
                    return this.members.Count;
                }
            }
        }

        public Activity Owner
        {
            get
            {
                if (this.Parent != null)
                {
                    return this.Parent[this.ParentId];
                }

                return null;
            }
        }

        public Activity this[int id]
        {
            get
            {
                var lookupId = id - 1;
                if (this.members == null || lookupId < 0 || lookupId >= this.members.Count)
                {
                    return null;
                }
                else
                {
                    return this.members[lookupId];
                }
            }
        }

        public void AddMember(Activity element)
        {
            if (this.members == null)
            {
                this.members = new List<Activity>();
            }

            if (lastId == int.MaxValue)
            {
                throw FxTrace.Exception.AsError(new NotSupportedException(SR.OutOfIdSpaceIds));
            }

            lastId++;
            
            // ID info is cleared inside InternalId.
            element.InternalId = lastId;
            Fx.Assert(element.MemberOf == this, "We should have already set this.");
            Fx.Assert(this.members.Count == element.InternalId - 1, "We should always be adding the next element");

            this.members.Add(element);
        }
        
        public void Dispose()
        {
            if (this.members != null)
            {
                this.members.Clear();
            }

            this.lastId = 0;
            this.Parent = null;
            this.ParentId = 0;
        }
    }
}


