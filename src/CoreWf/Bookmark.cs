// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Hosting;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Runtime.Serialization;

    [DataContract]
    [Fx.Tag.XamlVisible(false)]
    [TypeConverter(typeof(BookmarkConverter))]
    public partial class Bookmark : IEquatable<Bookmark>
    {
        private static IEqualityComparer<Bookmark> comparer;

        public Bookmark(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(name));
            }

            this.SerializedExternalName = name;
        }

        internal Bookmark()
        {
        }

        private Bookmark(long id)
        {
            Fx.Assert(id != 0, "id should not be zero");
            this.SerializedId = id;
        }

        public string Name => this.IsNamed ? this.SerializedExternalName : string.Empty;

        internal static Bookmark AsyncOperationCompletionBookmark { get; } = new Bookmark(-1);

        internal static IEqualityComparer<Bookmark> Comparer
        {
            get
            {
                if (comparer == null)
                {
                    comparer = new BookmarkComparer();
                }

                return comparer;
            }
        }

        internal ExclusiveHandleList ExclusiveHandles
        {
            get => this.SerializedExclusiveHandlesThatReferenceThis;
            set => this.SerializedExclusiveHandlesThatReferenceThis = value;
        }

        internal long Id
        {
            get
            {
                Fx.Assert(!this.IsNamed, "We should only get the id for unnamed bookmarks.");

                return this.SerializedId;
            }
        }

        internal bool IsNamed => this.SerializedId == 0;

        [DataMember(EmitDefaultValue = false)]
        internal BookmarkScope Scope { get; set; }

        /// <summary>
        /// Gets or sets the serialized exclusive handles that reference this.
        /// </summary>
        /// <value>The serialized exclusive handles that reference this.</value>
        /// <remarks>Used only when exclusive scopes are involved</remarks>
        [DataMember(EmitDefaultValue = false, Name = "exclusiveHandlesThatReferenceThis", Order = 2)]
        internal ExclusiveHandleList SerializedExclusiveHandlesThatReferenceThis { get; set; }

        [DataMember(EmitDefaultValue = false, Name = "externalName", Order = 1)]
        internal string SerializedExternalName { get; set; }

        [DataMember(EmitDefaultValue = false, Name = "id", Order = 0)]
        internal long SerializedId { get; set; }

        public bool Equals(Bookmark other) =>
            other is null
                ? false
                : this.IsNamed
                ? other.IsNamed && this.SerializedExternalName == other.SerializedExternalName
                : this.SerializedId == other.SerializedId;

        public override bool Equals(object obj) => this.Equals(obj as Bookmark);

        public override int GetHashCode() =>
            this.IsNamed ? this.SerializedExternalName.GetHashCode(StringComparison.Ordinal) : this.SerializedId.GetHashCode();

        public override string ToString() =>
            this.IsNamed ? this.Name : this.Id.ToString(CultureInfo.InvariantCulture);

        internal static Bookmark Create(long id) => new Bookmark(id);

        internal BookmarkInfo GenerateBookmarkInfo(BookmarkCallbackWrapper bookmarkCallback)
        {
            Fx.Assert(this.IsNamed, "Can only generate BookmarkInfo for external bookmarks");

            BookmarkScopeInfo scopeInfo = null;

            if (this.Scope != null)
            {
                scopeInfo = this.Scope.GenerateScopeInfo();
            }

            return new BookmarkInfo(this.SerializedExternalName, bookmarkCallback.ActivityInstance.Activity.DisplayName, scopeInfo);
        }
    }
}
