// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Activities.Runtime;
    using System.Runtime.Serialization;

    public sealed partial class ActivityInstance

#if NET45
#else
#endif
    {
        /// <summary>
        /// The ExtendedData class.
        /// </summary>
        /// <remarks>
        /// data necessary to support non-mainline usage of instances (i.e. creating bookmarks, using transactions)
        /// </remarks>
        [DataContract]
        internal partial class ExtendedData
        {
            private BookmarkList bookmarks;
            private ActivityReferenceList activityReferences;

            public ExtendedData()
            {
            }

            public int BlockingBookmarkCount { get; private set; }

            [DataMember(Name = XD.ActivityInstance.WaitingForTransactionContext, EmitDefaultValue = false)]
            public bool WaitingForTransactionContext { get; set; }

            [DataMember(Name = XD.ActivityInstance.FaultBookmark, EmitDefaultValue = false)]
            public FaultBookmark FaultBookmark { get; set; }

            public WorkflowDataContext DataContext { get; set; }

            [DataMember(Name = XD.ActivityInstance.BlockingBookmarkCount, EmitDefaultValue = false)]
            internal int SerializedBlockingBookmarkCount
            {
                get => this.BlockingBookmarkCount;
                set => this.BlockingBookmarkCount = value;
            }

            [DataMember(Name = XD.ActivityInstance.Bookmarks, EmitDefaultValue = false)]
            //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode, Justification = "Called from Serialization")]
            internal BookmarkList Bookmarks
            {
                get
                {
                    if (this.bookmarks == null || this.bookmarks.Count == 0)
                    {
                        return null;
                    }
                    else
                    {
                        return this.bookmarks;
                    }
                }
                set
                {
                    Fx.Assert(value != null, "We don't emit the default value so this should never be null.");
                    this.bookmarks = value;
                }
            }

            [DataMember(Name = XD.ActivityInstance.ActivityReferences, EmitDefaultValue = false)]
            //[SuppressMessage(FxCop.Category.Performance, FxCop.Rule.AvoidUncalledPrivateCode, Justification = "Called from Serialization")]
            internal ActivityReferenceList ActivityReferences
            {
                get
                {
                    if (this.activityReferences == null || this.activityReferences.Count == 0)
                    {
                        return null;
                    }
                    else
                    {
                        return this.activityReferences;
                    }
                }
                set
                {
                    Fx.Assert(value != null && value.Count > 0, "We shouldn't emit the default value or empty lists");
                    this.activityReferences = value;
                }
            }

            public bool HasActivityReferences => this.activityReferences != null && this.activityReferences.Count > 0;

            public void AddBookmark(Bookmark bookmark, bool affectsBusyCount)
            {
                if (this.bookmarks == null)
                {
                    this.bookmarks = new BookmarkList();
                }

                if (affectsBusyCount)
                {
                    this.BlockingBookmarkCount = this.BlockingBookmarkCount + 1;
                }

                this.bookmarks.Add(bookmark);
            }

            public void RemoveBookmark(Bookmark bookmark, bool affectsBusyCount)
            {
                Fx.Assert(this.bookmarks != null, "The bookmark list should have been initialized if we are trying to remove one.");

                if (affectsBusyCount)
                {
                    Fx.Assert(this.BlockingBookmarkCount > 0, "We should never decrement below zero.");

                    this.BlockingBookmarkCount = this.BlockingBookmarkCount - 1;
                }

                this.bookmarks.Remove(bookmark);
            }

            public void PurgeBookmarks(BookmarkScopeManager bookmarkScopeManager, BookmarkManager bookmarkManager, ActivityInstance owningInstance)
            {
                if (this.bookmarks != null)
                {
                    if (this.bookmarks.Count > 0)
                    {
                        this.bookmarks.TransferBookmarks(out var singleBookmark, out var multipleBookmarks);
                        this.bookmarks = null;

                        if (bookmarkScopeManager != null)
                        {
                            bookmarkScopeManager.PurgeBookmarks(bookmarkManager, singleBookmark, multipleBookmarks);
                        }
                        else
                        {
                            bookmarkManager.PurgeBookmarks(singleBookmark, multipleBookmarks);
                        }

                        // Clean up the busy count
                        owningInstance.DecrementBusyCount(this.BlockingBookmarkCount);
                        this.BlockingBookmarkCount = 0;
                    }
                }
            }

            public void AddActivityReference(ActivityInstanceReference reference)
            {
                if (this.activityReferences == null)
                {
                    this.activityReferences = new ActivityReferenceList();
                }

                this.activityReferences.Add(reference);
            }

            public void FillInstanceMap(ActivityInstanceMap instanceMap)
            {
                Fx.Assert(this.HasActivityReferences, "Must have references to have called this.");

                this.activityReferences.FillInstanceMap(instanceMap);
            }

            public void PurgeActivityReferences(ActivityInstanceMap instanceMap)
            {
                Fx.Assert(this.HasActivityReferences, "Must have references to have called this.");

                this.activityReferences.PurgeActivityReferences(instanceMap);
            }
        }
    }
}