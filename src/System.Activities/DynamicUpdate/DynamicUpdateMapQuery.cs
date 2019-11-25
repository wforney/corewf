// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.DynamicUpdate
{
    using System.Activities.Internals;
    using System.Activities.Runtime;

    public class DynamicUpdateMapQuery
    {
        private readonly DynamicUpdateMap map;
        private readonly Activity updatedWorkflowDefinition;
        private readonly Activity originalWorkflowDefinition;

        internal DynamicUpdateMapQuery(DynamicUpdateMap map, Activity updatedWorkflowDefinition, Activity originalWorkflowDefinition)
        {
            Fx.Assert(updatedWorkflowDefinition == updatedWorkflowDefinition.RootActivity, "This parameter must be root of workflow");
            Fx.Assert(originalWorkflowDefinition == originalWorkflowDefinition.RootActivity, "This parameter must be root of workflow");

            this.map = map;
            this.updatedWorkflowDefinition = updatedWorkflowDefinition;
            this.originalWorkflowDefinition = originalWorkflowDefinition;
        }

        public Activity FindMatch(Activity activity)
        {
            if (activity == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(activity));
            }

            if (this.IsInNewDefinition(activity))
            {
                return this.MatchNewToOld(activity);
            }
            else
            {
                return this.MatchOldToNew(activity);
            }
        }

        public Variable FindMatch(Variable variable)
        {
            if (variable == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(variable));
            }            

            if (this.IsInNewDefinition(variable))
            {
                return this.MatchNewToOld(variable);
            }
            else
            {
                return this.MatchOldToNew(variable);
            }
        }

        public bool CanApplyUpdateWhileRunning(Activity activity)
        {
            if (activity == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(activity));
            }

            return this.CanApplyUpdateWhileRunning(activity, this.IsInNewDefinition(activity));
        }

        private Activity MatchNewToOld(Activity newActivity) => this.MatchNewToOld(newActivity, out _);

        private Activity MatchNewToOld(Activity newActivity, out DynamicUpdateMapEntry entry)
        {
            if (this.map.TryGetUpdateEntryByNewId(newActivity.InternalId, out entry))
            {
                var rootIdSpace = this.map.IsForImplementation ? this.originalWorkflowDefinition.ParentOf : this.originalWorkflowDefinition.MemberOf;
                if (rootIdSpace != null)
                {
                    return rootIdSpace[entry.OldActivityId];
                }        
            }            

            return null;
        }

        private Variable MatchNewToOld(Variable newVariable)
        {
            if (!newVariable.IsPublic)
            {
                return null;
            }

            var oldOwner = this.MatchNewToOld(newVariable.Owner, out var entry);
            if (oldOwner == null)
            {
                return null;
            }

            var newIndex = newVariable.Owner.RuntimeVariables.IndexOf(newVariable);
            var oldIndex = entry.HasEnvironmentUpdates ?
                entry.EnvironmentUpdateMap.GetOldVariableIndex(newIndex) :
                newIndex;

            return oldIndex.HasValue ? oldOwner.RuntimeVariables[oldIndex.Value] : null;
        }

        private Activity MatchOldToNew(Activity oldActivity) => this.MatchOldToNew(oldActivity, out _);

        private Activity MatchOldToNew(Activity oldActivity, out DynamicUpdateMapEntry entry)
        {
            if (this.map.TryGetUpdateEntry(oldActivity.InternalId, out entry) && entry.NewActivityId > 0)
            {
                var rootIdSpace = this.map.IsForImplementation ? this.updatedWorkflowDefinition.ParentOf : this.updatedWorkflowDefinition.MemberOf;
                if (rootIdSpace != null)
                {
                    return rootIdSpace[entry.NewActivityId];
                }        
            }

            return null;
        }

        private Variable MatchOldToNew(Variable oldVariable)
        {
            if (!oldVariable.IsPublic)
            {
                return null;
            }

            var newOwner = this.MatchOldToNew(oldVariable.Owner, out var entry);
            if (newOwner == null)
            {
                return null;
            }

            var oldIndex = oldVariable.Owner.RuntimeVariables.IndexOf(oldVariable);
            var newIndex = entry.HasEnvironmentUpdates ?
                entry.EnvironmentUpdateMap.GetNewVariableIndex(oldIndex) :
                oldIndex;

            return newIndex.HasValue ? newOwner.RuntimeVariables[newIndex.Value] : null;
        }

        private bool CanApplyUpdateWhileRunning(Activity activity, bool isInNewDefinition)
        {
            var currentActivity = activity;
            var rootIdSpace = activity.MemberOf;
            do
            {
                DynamicUpdateMapEntry entry = null;
                if (isInNewDefinition)
                {
                    this.map.TryGetUpdateEntryByNewId(currentActivity.InternalId, out entry);
                }
                else if (currentActivity.MemberOf == rootIdSpace)
                {
                    this.map.TryGetUpdateEntry(currentActivity.InternalId, out entry);
                }

                if (entry != null &&
                    (entry.NewActivityId < 1 ||
                     entry.IsRuntimeUpdateBlocked ||
                     entry.IsUpdateBlockedByUpdateAuthor))
                {
                    return false;
                }

                currentActivity = currentActivity.Parent;
            }
            while (currentActivity != null && currentActivity.MemberOf == rootIdSpace);

            return true;
        }

        private bool IsInNewDefinition(Activity activity, bool isVariableOwner = false)
        {
            var result = false;
            if (activity.RootActivity == this.updatedWorkflowDefinition)
            {
                result = true;
            }
            else if (activity.RootActivity == this.originalWorkflowDefinition)
            {
                result = false;
            }
            else
            {
                ThrowNotInDefinition(isVariableOwner, 
                    SR.QueryVariableIsNotInDefinition,
                    SR.QueryActivityIsNotInDefinition);
            }

            // We only support either the public or the implementation IdSpace at the root of the workflow.
            // The user does not have visibility into nested IdSpaces so should not be querying into them.
            if (this.map.IsForImplementation)
            {
                if (activity.MemberOf.Owner != activity.RootActivity)
                {
                    ThrowNotInDefinition(isVariableOwner,
                        SR.QueryVariableIsPublic(activity.RootActivity),
                        SR.QueryActivityIsPublic(activity.RootActivity));
                }
            }
            else if (activity.MemberOf != activity.RootActivity.MemberOf)
            {
                ThrowNotInDefinition(isVariableOwner,
                    SR.QueryVariableIsInImplementation(activity.MemberOf.Owner),
                    SR.QueryActivityIsInImplementation(activity.MemberOf.Owner));
            }

            return result;
        }

        private bool IsInNewDefinition(Variable variable)
        {
            if (variable.Owner == null)
            {
                throw FxTrace.Exception.Argument(nameof(variable), SR.QueryVariableIsNotInDefinition);
            }

            if (!variable.IsPublic)
            {
                throw FxTrace.Exception.Argument(nameof(variable), SR.QueryVariableIsNotPublic);
            }

            return this.IsInNewDefinition(variable.Owner, true);
        }

        private static void ThrowNotInDefinition(bool isVariableOwner, string variableMessage, string activityMessage)
        {
            if (isVariableOwner)
            {
                throw FxTrace.Exception.Argument("variable", variableMessage);
            }
            else
            {
                throw FxTrace.Exception.Argument("activity", activityMessage);
            }
        }
    }
}
