// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using Portable.Xaml.Markup;

    using System.Activities;
    using System.Activities.DynamicUpdate;
    using System.Activities.Internals;
    using System.Activities.Runtime.Collections;
    using System.Collections.ObjectModel;
    using System.ComponentModel;

    [ContentProperty("Branches")]
    public sealed class Parallel : NativeActivity
    {
        private CompletionCallback<bool> onConditionComplete;
        private Collection<Activity> branches;
        private Collection<Variable> variables;
        private Variable<bool> hasCompleted;

        public Parallel()
            : base()
        {
        }

        public Collection<Variable> Variables
        {
            get
            {
                if (this.variables == null)
                {
                    this.variables = new ValidatingCollection<Variable>
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
                return this.variables;
            }
        }

        [DefaultValue(null)]
        [DependsOn("Variables")]
        public Activity<bool> CompletionCondition
        {
            get;
            set;
        }

        [DependsOn("CompletionCondition")]
        public Collection<Activity> Branches
        {
            get
            {
                if (this.branches == null)
                {
                    this.branches = new ValidatingCollection<Activity>
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
            if (updateContext.IsCancellationRequested || this.branches == null)
            {
                return;
            }

            if (this.CompletionCondition != null && updateContext.GetValue(this.hasCompleted))
            {
                // when CompletionCondition exists, schedule newly added branches only if "hasCompleted" variable evaluates to false
                return;
            }

            CompletionCallback onBranchComplete = new CompletionCallback(OnBranchComplete);

            foreach (Activity branch in this.branches)
            {
                if (updateContext.IsNewlyAdded(branch))
                {
                    updateContext.ScheduleActivity(branch, onBranchComplete);
                }
            }
        } 

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            var children = new Collection<Activity>();

            foreach (var branch in this.Branches)
            {
                children.Add(branch);
            }

            if (this.CompletionCondition != null)
            {
                children.Add(this.CompletionCondition);
            }

            metadata.SetChildrenCollection(children);

            metadata.SetVariablesCollection(this.Variables);

            if (this.CompletionCondition != null)
            {
                if (this.hasCompleted == null)
                {
                    this.hasCompleted = new Variable<bool>("hasCompletedVar");
                }

                metadata.AddImplementationVariable(this.hasCompleted);
            }
        }

        protected override void Execute(NativeActivityContext context)
        {
            if (this.branches != null && this.Branches.Count != 0)
            {
                var onBranchComplete = new CompletionCallback(OnBranchComplete);

                for (var i = this.Branches.Count - 1; i >= 0; i--)
                {
                    context.ScheduleActivity(this.Branches[i], onBranchComplete);
                }
            }
        }

        protected override void Cancel(NativeActivityContext context)
        {
            // If we don't have a completion condition then we can just
            // use default logic.
            if (this.CompletionCondition == null)
            {
                base.Cancel(context);
            }
            else
            {
                context.CancelChildren();
            }
        }

        private void OnBranchComplete(NativeActivityContext context, ActivityInstance completedInstance)
        {
            if (this.CompletionCondition != null && !this.hasCompleted.Get(context))
            {
                // If we haven't completed, we've been requested to cancel, and we've had a child
                // end in a non-Closed state then we should cancel ourselves.
                if (completedInstance.State != ActivityInstanceState.Closed && context.IsCancellationRequested)
                {
                    context.MarkCanceled();
                    this.hasCompleted.Set(context, true);
                    return;
                }

                if (this.onConditionComplete == null)
                {
                    this.onConditionComplete = new CompletionCallback<bool>(OnConditionComplete);
                }

                context.ScheduleActivity(this.CompletionCondition, this.onConditionComplete);
            }
        }

        private void OnConditionComplete(NativeActivityContext context, ActivityInstance completedInstance, bool result)
        {
            if (result)
            {
                context.CancelChildren();
                this.hasCompleted.Set(context, true);
            }
        }
    }
}
