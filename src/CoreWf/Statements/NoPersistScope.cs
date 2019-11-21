// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using System.Activities;
    using System.Activities.Validation;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows.Markup;
    using System.Activities.Runtime;

    [ContentProperty("Body")]
    public sealed class NoPersistScope : NativeActivity
    {
        private static Constraint constraint;
        private readonly Variable<NoPersistHandle> noPersistHandle;

        public NoPersistScope()
        {
            this.noPersistHandle = new Variable<NoPersistHandle>();
            this.Constraints.Add(NoPersistScope.Constraint);
        }

        [DefaultValue(null)]
        public Activity Body
        {
            get;
            set;
        }

        private static Constraint Constraint
        {
            get
            {
                if (constraint == null)
                {
                    constraint = NoPersistScope.NoPersistInScope();
                }

                return constraint;
            }
        }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            metadata.AddChild(this.Body);
            metadata.AddImplementationVariable(this.noPersistHandle);
        }

        protected override void Execute(NativeActivityContext context)
        {
            if (this.Body != null)
            {
                var handle = this.noPersistHandle.Get(context);
                handle.Enter(context);
                context.ScheduleActivity(this.Body);
            }
        }

        private static Constraint NoPersistInScope()
        {
            var validationContext = new DelegateInArgument<ValidationContext>("validationContext");
            var noPersistScope = new DelegateInArgument<NoPersistScope>("noPersistScope");
            var isConstraintSatisfied = new Variable<bool>("isConstraintSatisfied", true);
            var childActivities = new Variable<IEnumerable<Activity>>("childActivities");
            var constraintViolationMessage = new Variable<string>("constraintViolationMessage");

            return new Constraint<NoPersistScope>
            {
                Body = new ActivityAction<NoPersistScope, ValidationContext>
                {
                    Argument1 = noPersistScope,
                    Argument2 = validationContext,
                    Handler = new Sequence
                    {
                        Variables =
                        {
                            isConstraintSatisfied,
                            childActivities,
                            constraintViolationMessage,
                        },
                        Activities =
                        {
                            new Assign<IEnumerable<Activity>>
                            {
                                To = childActivities,
                                Value = new GetChildSubtree
                                {
                                    ValidationContext = validationContext,
                                },
                            },
                            new Assign<bool>
                            {
                                To = isConstraintSatisfied,
                                Value = new CheckNoPersistInDescendants
                                {
                                    NoPersistScope = noPersistScope,
                                    DescendantActivities = childActivities,
                                    ConstraintViolationMessage = constraintViolationMessage,
                                },
                            },
                            new AssertValidation
                            {
                                Assertion = isConstraintSatisfied,
                                Message = constraintViolationMessage,
                            },
                        }
                    }
                }
            };
        }

        private sealed class CheckNoPersistInDescendants : CodeActivity<bool>
        {
            [RequiredArgument]
            public InArgument<NoPersistScope> NoPersistScope { get; set; }

            [RequiredArgument]
            public InArgument<IEnumerable<Activity>> DescendantActivities { get; set; }

            [RequiredArgument]
            public OutArgument<string> ConstraintViolationMessage { get; set; }

            protected override void CacheMetadata(CodeActivityMetadata metadata)
            {
                var runtimeArguments = new Collection<RuntimeArgument>();

                var noPersistScopeArgument = new RuntimeArgument("NoPersistScope", typeof(NoPersistScope), ArgumentDirection.In);
                metadata.Bind(this.NoPersistScope, noPersistScopeArgument);
                runtimeArguments.Add(noPersistScopeArgument);

                var descendantActivitiesArgument = new RuntimeArgument("DescendantActivities", typeof(IEnumerable<Activity>), ArgumentDirection.In);
                metadata.Bind(this.DescendantActivities, descendantActivitiesArgument);
                runtimeArguments.Add(descendantActivitiesArgument);

                var constraintViolationMessageArgument = new RuntimeArgument("ConstraintViolationMessage", typeof(string), ArgumentDirection.Out);
                metadata.Bind(this.ConstraintViolationMessage, constraintViolationMessageArgument);
                runtimeArguments.Add(constraintViolationMessageArgument);

                var resultArgument = new RuntimeArgument("Result", typeof(bool), ArgumentDirection.Out);
                metadata.Bind(this.Result, resultArgument);
                runtimeArguments.Add(resultArgument);

                metadata.SetArgumentsCollection(runtimeArguments);
            }

            protected override bool Execute(CodeActivityContext context)
            {
                var descendantActivities = this.DescendantActivities.Get(context);
                Fx.Assert(descendantActivities != null, "this.DescendantActivities cannot evaluate to null.");

                var firstPersist = descendantActivities.OfType<Persist>().FirstOrDefault();
                if (firstPersist != null)
                {
                    var noPersistScope = this.NoPersistScope.Get(context);
                    Fx.Assert(noPersistScope != null, "this.NoPersistScope cannot evaluate to null.");

                    var constraintViolationMessage = SR.NoPersistScopeCannotContainPersist(noPersistScope.DisplayName, firstPersist.DisplayName);
                    this.ConstraintViolationMessage.Set(context, constraintViolationMessage);
                    return false;
                }

                return true;
            }
        }
    }
}
