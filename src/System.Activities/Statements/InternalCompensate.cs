// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.
namespace System.Activities.Statements
{
    using System.Collections.ObjectModel;
    using System.Activities.Runtime;

    internal sealed class InternalCompensate : NativeActivity
    {
        public InternalCompensate()
            : base()
        {
        }

        public InArgument<CompensationToken> Target
        {
            get;
            set;
        }

        protected override bool CanInduceIdle
        {
            get
            {
                return true;
            }
        }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            var targetArgument = new RuntimeArgument("Target", typeof(CompensationToken), ArgumentDirection.In);
            metadata.Bind(this.Target, targetArgument);
            metadata.SetArgumentsCollection(new Collection<RuntimeArgument> { targetArgument });
        }

        protected override void Execute(NativeActivityContext context)
        {
            var compensationExtension = context.GetExtension<CompensationExtension>();
            Fx.Assert(compensationExtension != null, "CompensationExtension must be valid");

            var compensationToken = Target.Get(context);
            Fx.Assert(compensationToken != null, "CompensationToken must be valid");

            // The compensationToken should be a valid one at this point. Ensure its validated in Compensate activity.
            var tokenData = compensationExtension.Get(compensationToken.CompensationId);
            Fx.Assert(tokenData != null, "The compensationToken should be a valid one at this point. Ensure its validated in Compensate activity.");

            Fx.Assert(tokenData.BookmarkTable[CompensationBookmarkName.Compensated] == null, "Bookmark should not be already initialized in the bookmark table.");
            tokenData.BookmarkTable[CompensationBookmarkName.Compensated] = context.CreateBookmark(new BookmarkCallback(OnCompensated));

            tokenData.CompensationState = CompensationState.Compensating;
            compensationExtension.NotifyMessage(context, tokenData.CompensationId, CompensationBookmarkName.OnCompensation);
        }

        // Successfully received Compensated response. 
        private void OnCompensated(NativeActivityContext context, Bookmark bookmark, object value)
        {
            var compensationExtension = context.GetExtension<CompensationExtension>();
            Fx.Assert(compensationExtension != null, "CompensationExtension must be valid");

            var compensationToken = Target.Get(context);
            Fx.Assert(compensationToken != null, "CompensationToken must be valid");

            var tokenData = compensationExtension.Get(compensationToken.CompensationId);
            Fx.Assert(tokenData != null, "The compensationToken should be a valid one at this point. Ensure its validated in Compensate activity.");

            tokenData.CompensationState = CompensationState.Compensated;
            if (TD.CompensationStateIsEnabled())
            {
                TD.CompensationState(tokenData.DisplayName, tokenData.CompensationState.ToString());
            }

            // Remove the token from the parent! 
            if (tokenData.ParentCompensationId != CompensationToken.RootCompensationId)
            {
                var parentToken = compensationExtension.Get(tokenData.ParentCompensationId);
                Fx.Assert(parentToken != null, "parentToken must be valid");

                parentToken.ExecutionTracker.Remove(tokenData);
            }
            else
            {
                // remove from workflow root...
                var parentToken = compensationExtension.Get(CompensationToken.RootCompensationId);
                Fx.Assert(parentToken != null, "parentToken must be valid");

                parentToken.ExecutionTracker.Remove(tokenData);
            }

            tokenData.RemoveBookmark(context, CompensationBookmarkName.Compensated);

            // Remove the token from the extension...
            compensationExtension.Remove(compensationToken.CompensationId);
        }

        protected override void Cancel(NativeActivityContext context)
        {
            // Suppress Cancel   
        }
    }
}

