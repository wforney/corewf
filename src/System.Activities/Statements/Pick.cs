// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using Portable.Xaml.Markup;

    using System.Activities.DynamicUpdate;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.Collections;
    using System.Activities.Validation;
    using System.Collections.ObjectModel;
    using System.Runtime.Serialization;

    [ContentProperty("Branches")]
    public sealed class Pick : NativeActivity
    {
        private const string pickStateProperty = "System.Activities.Statements.Pick.PickState";
        private Collection<PickBranch> branches;
        private readonly Variable<PickState> pickStateVariable;
        private Collection<Activity> branchBodies;        
        
        public Pick()
        {
            this.pickStateVariable = new Variable<PickState>();
        }

        protected override bool CanInduceIdle
        {
            get
            {
                return true;
            }
        }

        public Collection<PickBranch> Branches
        {
            get
            {
                if (this.branches == null)
                {
                    this.branches = new ValidatingCollection<PickBranch>
                    {
                        // disallow null values
                        OnAddValidationCallback = item =>
                        {
                            if (item == null)
                            {
                                throw FxTrace.Exception.ArgumentNull(nameof(item));
                            }
                        }
                    };
                }
                return this.branches;
            }
        }


        protected override void OnCreateDynamicUpdateMap(NativeActivityUpdateMapMetadata metadata, Activity originalActivity)
        {
            metadata.AllowUpdateInsideThisActivity();
        }

        protected override void UpdateInstance(NativeActivityUpdateContext updateContext)
        {
            PickState pickState = updateContext.GetValue(this.pickStateVariable);
            Fx.Assert(pickState != null, "Pick's Execute must have run by now.");

            if (updateContext.IsCancellationRequested || pickState.TriggerCompletionBookmark == null)
            {
                // do not schedule newly added Branches once a Trigger has successfully completed.
                return;
            }

            CompletionCallback onBranchCompleteCallback = new CompletionCallback(OnBranchComplete);
            foreach (PickBranchBody body in this.branchBodies)
            {
                if (updateContext.IsNewlyAdded(body))
                {
                    updateContext.ScheduleActivity(body, onBranchCompleteCallback, null);
                }
            }
        } 

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            if (this.branchBodies == null)
            {
                this.branchBodies = new Collection<Activity>();
            }
            else
            {
                this.branchBodies.Clear();
            }

            foreach (var branch in this.Branches)
            {
                if (branch.Trigger == null)
                {
                    metadata.AddValidationError(new ValidationError(SR.PickBranchRequiresTrigger(branch.DisplayName), false, null, branch));
                }
                
                var pickBranchBody = new PickBranchBody
                {
                    Action = branch.Action,
                    DisplayName = branch.DisplayName,
                    Trigger = branch.Trigger,
                    Variables = branch.Variables,                    
                };

                this.branchBodies.Add(pickBranchBody);

                metadata.AddChild(pickBranchBody, origin: branch);
            }
                        
            metadata.AddImplementationVariable(this.pickStateVariable);
        }

        protected override void Execute(NativeActivityContext context)
        {
            if (this.branchBodies.Count == 0)
            {
                 return;
            }

            var pickState = new PickState();
            this.pickStateVariable.Set(context, pickState);

            pickState.TriggerCompletionBookmark = context.CreateBookmark(new BookmarkCallback(OnTriggerComplete));

            context.Properties.Add(pickStateProperty, pickState);

            var onBranchCompleteCallback = new CompletionCallback(OnBranchComplete);

            //schedule every branch to only run trigger
            for (var i = this.branchBodies.Count - 1; i >= 0; i--)
            {
                context.ScheduleActivity(this.branchBodies[i], onBranchCompleteCallback);
            }
        }

        protected override void Cancel(NativeActivityContext context)
        {
            context.CancelChildren();
        }

        private void OnBranchComplete(NativeActivityContext context, ActivityInstance completedInstance)
        {
            var pickState = this.pickStateVariable.Get(context);
            var executingChildren = context.GetChildren();

            switch (completedInstance.State)
            {
                case ActivityInstanceState.Closed:
                    pickState.HasBranchCompletedSuccessfully = true;
                    break;
                case ActivityInstanceState.Canceled:
                case ActivityInstanceState.Faulted:
                    if (context.IsCancellationRequested)
                    {
                        if (executingChildren.Count == 0 && !pickState.HasBranchCompletedSuccessfully)
                        {
                            // All of the branches are complete and we haven't had a single
                            // one complete successfully and we've been asked to cancel.
                            context.MarkCanceled();
                            context.RemoveAllBookmarks();
                        }
                    }                    
                    break;
            }

            //the last branch should always resume action bookmark if it's still there
            if (executingChildren.Count == 1 && pickState.ExecuteActionBookmark != null)
            {
                ResumeExecutionActionBookmark(pickState, context);
            }
        }

        private void OnTriggerComplete(NativeActivityContext context, Bookmark bookmark, object state)
        {
            var pickState = this.pickStateVariable.Get(context);

            var winningBranch = (string)state;

            var children = context.GetChildren();

            var resumeAction = true;

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];

                if (child.Id != winningBranch)
                {
                    context.CancelChild(child);
                    resumeAction = false;
                }
            }
            
            if (resumeAction)
            {
                ResumeExecutionActionBookmark(pickState, context);
            }
        }

        private void ResumeExecutionActionBookmark(PickState pickState, NativeActivityContext context)
        {
            Fx.Assert(pickState.ExecuteActionBookmark != null, "This should have been set by the branch.");

            context.ResumeBookmark(pickState.ExecuteActionBookmark, null);
            pickState.ExecuteActionBookmark = null;
        }

        [DataContract]
        internal class PickState
        {
            [DataMember(EmitDefaultValue = false)]
            public bool HasBranchCompletedSuccessfully
            {
                get;
                set;
            }

            [DataMember(EmitDefaultValue = false)]
            public Bookmark TriggerCompletionBookmark
            {
                get;
                set;
            }

            [DataMember(EmitDefaultValue = false)]
            public Bookmark ExecuteActionBookmark
            {
                get;
                set;
            }
        }

        private class PickBranchBody : NativeActivity
        {
            public PickBranchBody()
            {
            }

            protected override bool CanInduceIdle
            {
                get
                {
                    return true;
                }
            }

            public Collection<Variable> Variables
            {
                get;
                set;
            }

            public Activity Trigger
            {
                get;
                set;
            }

            public Activity Action
            {
                get;
                set;
            }


            protected override void OnCreateDynamicUpdateMap(NativeActivityUpdateMapMetadata metadata, Activity originalActivity)
            {
                PickBranchBody originalBranchBody = (PickBranchBody)originalActivity;
                if ((originalBranchBody.Action != null && metadata.GetMatch(this.Trigger) == originalBranchBody.Action) || (this.Action != null && metadata.GetMatch(this.Action) == originalBranchBody.Trigger))
                {
                    metadata.DisallowUpdateInsideThisActivity(SR.PickBranchTriggerActionSwapped);
                    return;
                }

                metadata.AllowUpdateInsideThisActivity();
            } 

            protected override void CacheMetadata(NativeActivityMetadata metadata)
            {
                Collection<Activity> children = null;

                if (this.Trigger != null)
                {
                    ActivityUtilities.Add(ref children, this.Trigger);
                }
                if (this.Action != null)
                {
                    ActivityUtilities.Add(ref children, this.Action);
                }

                metadata.SetChildrenCollection(children);
                
                metadata.SetVariablesCollection(this.Variables);
            }

            protected override void Execute(NativeActivityContext context)
            {
                Fx.Assert(this.Trigger != null, "We validate that the trigger is not null in Pick.CacheMetadata");

                context.ScheduleActivity(this.Trigger, new CompletionCallback(OnTriggerCompleted));
            }

            private void OnTriggerCompleted(NativeActivityContext context, ActivityInstance completedInstance)
            {
                var pickState = (PickState)context.Properties.Find(pickStateProperty);

                if (completedInstance.State == ActivityInstanceState.Closed && pickState.TriggerCompletionBookmark != null)
                {
                    // We're the first trigger!  We win!
                    context.ResumeBookmark(pickState.TriggerCompletionBookmark, context.ActivityInstanceId);
                    pickState.TriggerCompletionBookmark = null;
                    pickState.ExecuteActionBookmark = context.CreateBookmark(new BookmarkCallback(OnExecuteAction));
                }
                else if (!context.IsCancellationRequested)
                {
                    // We didn't win, but we haven't been requested to cancel yet.
                    // We'll just create a bookmark to keep ourselves from completing.
                    context.CreateBookmark();
                }
                // else
                // {
                //     No need for an else since default cancelation will cover it!
                // }
            }

            private void OnExecuteAction(NativeActivityContext context, Bookmark bookmark, object state)
            {
                if (this.Action != null)
                {
                    context.ScheduleActivity(this.Action);
                }
            }
        }
    }
}
