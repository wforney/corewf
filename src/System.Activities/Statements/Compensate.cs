// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.
namespace System.Activities.Statements
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Activities.Validation;
    using System.Linq;
    using System.Activities.Expressions;
    using System.Activities.Runtime;
    using System.Activities.Internals;

    public sealed class Compensate : NativeActivity
    {
        private static readonly Constraint compensateWithNoTarget = Compensate.CompensateWithNoTarget();
        private InternalCompensate internalCompensate;
        private DefaultCompensation defaultCompensation;
        private readonly Variable<CompensationToken> currentCompensationToken;

        public Compensate()
            : base()
        {
            this.currentCompensationToken = new Variable<CompensationToken>();
        }

        [DefaultValue(null)]
        public InArgument<CompensationToken> Target
        {
            get;
            set;
        }

        private DefaultCompensation DefaultCompensation
        {
            get
            {
                if (this.defaultCompensation == null)
                {
                    this.defaultCompensation = new DefaultCompensation()
                        {
                            Target = new InArgument<CompensationToken>(this.currentCompensationToken),
                        };
                }

                return this.defaultCompensation;
            }
        }

        private InternalCompensate InternalCompensate
        {
            get
            {
                if (this.internalCompensate == null)
                {
                    this.internalCompensate = new InternalCompensate()
                        {
                            Target = new InArgument<CompensationToken>(new ArgumentValue<CompensationToken> { ArgumentName = "Target" }),
                        };
                }

                return this.internalCompensate;
            }
        }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            var targetArgument = new RuntimeArgument("Target", typeof(CompensationToken), ArgumentDirection.In);
            metadata.Bind(this.Target, targetArgument);
            metadata.SetArgumentsCollection(new Collection<RuntimeArgument> { targetArgument });

            metadata.SetImplementationVariablesCollection(new Collection<Variable> { this.currentCompensationToken });

            Fx.Assert(DefaultCompensation != null, "DefaultCompensation must be valid");
            Fx.Assert(InternalCompensate != null, "InternalCompensate must be valid");
            metadata.SetImplementationChildrenCollection(
                new Collection<Activity>
                {
                    DefaultCompensation,
                    InternalCompensate
                });
        }

        internal override IList<Constraint> InternalGetConstraints()
        {
            return new List<Constraint>(1) { compensateWithNoTarget };
        }

        private static Constraint CompensateWithNoTarget()
        {
            var element = new DelegateInArgument<Compensate> { Name = "element" };
            var validationContext = new DelegateInArgument<ValidationContext> { Name = "validationContext" };
            var assertFlag = new Variable<bool> { Name = "assertFlag" };
            var elements = new Variable<IEnumerable<Activity>>() { Name = "elements" };
            var index = new Variable<int>() { Name = "index" };

            return new Constraint<Compensate>
            {
                Body = new ActivityAction<Compensate, ValidationContext>
                {
                    Argument1 = element,
                    Argument2 = validationContext,
                    Handler = new Sequence
                    {
                        Variables = 
                        {
                            assertFlag,
                            elements,
                            index
                        },
                        Activities =
                        {
                            new If
                            {
                                Condition = new InArgument<bool>((env) => element.Get(env).Target != null),
                                Then = new Assign<bool>
                                {
                                    To = assertFlag,
                                    Value = true
                                },
                                Else = new Sequence
                                {
                                    Activities = 
                                    {
                                        new Assign<IEnumerable<Activity>>
                                        {
                                            To = elements,
                                            Value = new GetParentChain
                                            {
                                                ValidationContext = validationContext,
                                            },
                                        },
                                        new While(env => (assertFlag.Get(env) != true) && index.Get(env) < elements.Get(env).Count())
                                        {
                                            Body = new Sequence
                                            {
                                                Activities = 
                                                {
                                                    new If(env => (elements.Get(env).ElementAt(index.Get(env))).GetType() == typeof(CompensationParticipant))
                                                    {
                                                        Then = new Assign<bool>
                                                        {
                                                            To = assertFlag,
                                                            Value = true                                                            
                                                        },
                                                    },
                                                    new Assign<int>
                                                    {
                                                        To = index,
                                                        Value = new InArgument<int>(env => index.Get(env) + 1)
                                                    },
                                                }
                                            }
                                        }
                                    }
                                }                                
                            },
                            new AssertValidation
                            {
                                Assertion = new InArgument<bool>(assertFlag),
                                Message = new InArgument<string>(SR.CompensateWithNoTargetConstraint)   
                            }
                        }
                    }
                }
            };
        }

        protected override void Execute(NativeActivityContext context)
        {
            var compensationExtension = context.GetExtension<CompensationExtension>();
            if (compensationExtension == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CompensateWithoutCompensableActivity(this.DisplayName)));
            }

            if (Target.IsEmpty)
            {
                var ambientCompensationToken = (CompensationToken)context.Properties.Find(CompensationToken.PropertyName);
                var ambientTokenData = ambientCompensationToken == null ? null : compensationExtension.Get(ambientCompensationToken.CompensationId);

                if (ambientTokenData != null && ambientTokenData.IsTokenValidInSecondaryRoot)
                {
                    this.currentCompensationToken.Set(context, ambientCompensationToken);
                    if (ambientTokenData.ExecutionTracker.Count > 0)
                    {
                        context.ScheduleActivity(DefaultCompensation);
                    }
                }
                else
                {
                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.InvalidCompensateActivityUsage(this.DisplayName)));
                }
            }
            else
            {
                var compensationToken = Target.Get(context);
                var tokenData = compensationToken == null ? null : compensationExtension.Get(compensationToken.CompensationId);

                if (compensationToken == null)
                {
                    throw FxTrace.Exception.Argument("Target", SR.InvalidCompensationToken(this.DisplayName));
                }

                if (compensationToken.CompensateCalled)
                {
                    // No-Op
                    return;
                }

                if (tokenData == null || tokenData.CompensationState != CompensationState.Completed)
                {
                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CompensableActivityAlreadyConfirmedOrCompensated));
                }

                // A valid in-arg was passed...            
                tokenData.CompensationState = CompensationState.Compensating;
                compensationToken.CompensateCalled = true;
                context.ScheduleActivity(InternalCompensate);
            }
        }

        protected override void Cancel(NativeActivityContext context)
        {
            // Suppress Cancel   
        }
    }
}
