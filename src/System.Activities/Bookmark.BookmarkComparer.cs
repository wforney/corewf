// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    public partial class Bookmark
    {
        [DataContract]
        internal class BookmarkComparer : IEqualityComparer<Bookmark>
        {
            public BookmarkComparer()
            {
            }

            public bool Equals(Bookmark x, Bookmark y) => x is null ? y is null : x.Equals(y);

            public int GetHashCode(Bookmark obj) => obj.GetHashCode();
        }
    }
}
