// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using Portable.Xaml.Markup;

    using System.Activities;
    using System.Activities.Internals;
    using System.Activities.Runtime.Collections;
    using System.Collections.ObjectModel;

    [ContentProperty("Activities")]
    public sealed class Sequence : NativeActivity
    {
        private Collection<Activity> activities;
        private Collection<Variable> variables;
        private readonly Variable<int> lastIndexHint;
        private readonly CompletionCallback onChildComplete;

        public Sequence()
            : base()
        {
            this.lastIndexHint = new Variable<int>();
            this.onChildComplete = new CompletionCallback(InternalExecute);
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

        [DependsOn("Variables")]
        public Collection<Activity> Activities
        {
            get
            {
                if (this.activities == null)
                {
                    this.activities = new ValidatingCollection<Activity>
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
                return this.activities;
            }
        }


        protected override void OnCreateDynamicUpdateMap(DynamicUpdate.NativeActivityUpdateMapMetadata metadata, Activity originalActivity)
        {
            // Our algorithm for recovering from update depends on iterating a unique Activities list.
            // So we can't support update if the same activity is referenced more than once.
            for (int i = 0; i < this.Activities.Count - 1; i++)
            {
                for (int j = i + 1; j < this.Activities.Count; j++)
                {
                    if (this.Activities[i] == this.Activities[j])
                    {
                        metadata.DisallowUpdateInsideThisActivity(SR.SequenceDuplicateReferences);
                        break;
                    }
                }
            }
        } 

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            metadata.SetChildrenCollection(this.Activities);
            metadata.SetVariablesCollection(this.Variables);
            metadata.AddImplementationVariable(this.lastIndexHint);
        }

        protected override void Execute(NativeActivityContext context)
        {
            if (this.activities != null && this.Activities.Count > 0)
            {
                var nextChild = this.Activities[0];

                context.ScheduleActivity(nextChild, this.onChildComplete);
            }
        }

        private void InternalExecute(NativeActivityContext context, ActivityInstance completedInstance)
        {
            var completedInstanceIndex = this.lastIndexHint.Get(context);

            if (completedInstanceIndex >= this.Activities.Count || this.Activities[completedInstanceIndex] != completedInstance.Activity)
            {
                completedInstanceIndex = this.Activities.IndexOf(completedInstance.Activity);
            }

            var nextChildIndex = completedInstanceIndex + 1;

            if (nextChildIndex == this.Activities.Count)
            {
                return;
            }

            var nextChild = this.Activities[nextChildIndex];

            context.ScheduleActivity(nextChild, this.onChildComplete);

            this.lastIndexHint.Set(context, nextChildIndex);
        }
    }
}
