// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Validation;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;

    public sealed class Rethrow : NativeActivity
    {
        public Rethrow()
        {
            var element = new DelegateInArgument<Rethrow>() { Name = "constraintArg" };
            var validationContext = new DelegateInArgument<ValidationContext>() { Name = "validationContext" };
            base.Constraints.Add(new Constraint<Rethrow>
            {
                Body = new ActivityAction<Rethrow, ValidationContext>
                {
                    Argument1 = element,
                    Argument2 = validationContext,
                    Handler = new RethrowBuildConstraint
                    {
                        ParentChain = new GetParentChain
                        {
                            ValidationContext = validationContext,
                        },
                        RethrowActivity = element
                    },
                }
            });
        }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
        }

        protected override void Execute(NativeActivityContext context)
        {

            if (!(context.Properties.Find(TryCatch.FaultContextId) is FaultContext faultContext))
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.FaultContextNotFound(this.DisplayName)));
            }

            context.RethrowException(faultContext);
        }

        private class RethrowBuildConstraint : NativeActivity<bool>
        {
            [RequiredArgument]
            [DefaultValue(null)]
            public InArgument<IEnumerable<Activity>> ParentChain
            {
                get;
                set;
            }

            [RequiredArgument]
            [DefaultValue(null)]
            public InArgument<Rethrow> RethrowActivity
            {
                get;
                set;
            }

            protected override void CacheMetadata(NativeActivityMetadata metadata)
            {
                var parentChainArgument = new RuntimeArgument("ParentChain", typeof(IEnumerable<Activity>), ArgumentDirection.In, true);
                metadata.Bind(this.ParentChain, parentChainArgument);
                metadata.AddArgument(parentChainArgument);

                var rethrowActivityArgument = new RuntimeArgument("RethrowActivity", typeof(Rethrow), ArgumentDirection.In, true);
                metadata.Bind(this.RethrowActivity, rethrowActivityArgument);
                metadata.AddArgument(rethrowActivityArgument);
            }

            protected override void Execute(NativeActivityContext context)
            {
                var parentChain = this.ParentChain.Get(context);
                var rethrowActivity = this.RethrowActivity.Get(context);
                Activity previousActivity = rethrowActivity;
                var privateRethrow = false;

                // TryCatch with Rethrow is usually authored in the following way:
                // 
                // TryCatch
                // {
                //   Try = DoWork
                //   Catch Handler = Sequence
                //                   { 
                //                     ProcessException,
                //                     Rethrow
                //                   }
                // }
                // Notice that the chain of Activities is TryCatch->Sequence->Rethrow
                // We want to validate that Rethrow is in the catch block of TryCatch
                // We walk up the parent chain until we find TryCatch.  Then we check if one the catch handlers points to Sequence(the previous activity in the tree)
                foreach (var parent in parentChain)
                {
                    // Rethrow is only allowed under the public children of a TryCatch activity.
                    // If any of the activities in the tree is a private child, report a constraint violation.
                    if (parent.ImplementationChildren.Contains(previousActivity))
                    {
                        privateRethrow = true;
                    }

                    if (parent is TryCatch tryCatch)
                    {
                        if (previousActivity != null)
                        {
                            foreach (var catchHandler in tryCatch.Catches)
                            {
                                var catchAction = catchHandler.GetAction();
                                if (catchAction != null && catchAction.Handler == previousActivity)
                                {
                                    if (privateRethrow)
                                    {
                                        Constraint.AddValidationError(context, new ValidationError(SR.RethrowMustBeAPublicChild(rethrowActivity.DisplayName), rethrowActivity));
                                    }
                                    return;
                                }
                            }
                        }
                    }

                    previousActivity = parent;
                }

                Constraint.AddValidationError(context, new ValidationError(SR.RethrowNotInATryCatch(rethrowActivity.DisplayName), rethrowActivity));
            }
        }
    }
}

