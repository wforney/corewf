// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Activities.DynamicUpdate;
    using System.Activities.Runtime;

    public sealed partial class ActivityInstance
    {
        internal enum Substate : byte
        {
            Executing = 0, // choose the most common persist-time state for the default
            PreExecuting = 0x80, // used for all states prior to "core execution"
            Created = 1 | Substate.PreExecuting,
            ResolvingArguments = 2 | Substate.PreExecuting,

            // ResolvedArguments = 2,
            ResolvingVariables = 3 | Substate.PreExecuting,

            // ResolvedVariables = 3,
            Initialized = 4 | Substate.PreExecuting,

            Canceling = 5,
        }

        void ActivityInstanceMap.IActivityReferenceWithEnvironment.UpdateEnvironment(EnvironmentUpdateMap map, Activity activity)
        {
            Fx.Assert(this.SubState != Substate.ResolvingVariables, "We must have already performed the same validations in advance.");
            Fx.Assert(this.SubState != Substate.ResolvingArguments, "We must have already performed the same validations in advance.");

            if (this.SerializedNoSymbols)
            {
                // create a new LocationReference and this ActivityInstance becomes the owner of the
                // created environment.
                var oldParentEnvironment = this.environment;

                Fx.Assert(oldParentEnvironment != null, "environment must never be null.");

                this.environment = new LocationEnvironment(oldParentEnvironment, map.NewArgumentCount + map.NewVariableCount + map.NewPrivateVariableCount + map.RuntimeDelegateArgumentCount);
                this.SerializedNoSymbols = false;

                // traverse the activity instance chain. Update all its non-environment-owning
                // decedent instances to point to the newly created enviroment, and, update all its
                // environment-owning decendent instances to have their environment's parent to
                // point to the newly created environment.
                UpdateLocationEnvironmentHierarchy(oldParentEnvironment, this.environment, this);
            }

            this.Environment.Update(map, activity);
        }
    }
}
