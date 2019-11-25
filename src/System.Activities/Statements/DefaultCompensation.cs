// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.
namespace System.Activities.Statements
{
    using System;
    using System.Collections.ObjectModel;
    using System.Activities.Runtime;
    using System.Activities.Internals;

    internal sealed class DefaultCompensation : NativeActivity
    {
        private readonly Activity body;
        private readonly Variable<CompensationToken> toCompensateToken;
        private CompletionCallback onChildCompensated;

        public DefaultCompensation()
            : base()
        {
            this.toCompensateToken = new Variable<CompensationToken>();

            this.body = new InternalCompensate()
                {
                    Target = new InArgument<CompensationToken>(toCompensateToken),
                };
        }

        public InArgument<CompensationToken> Target
        {
            get;
            set;
        }

        private Activity Body
        {
            get { return this.body; }
        }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            var targetArgument = new RuntimeArgument("Target", typeof(CompensationToken), ArgumentDirection.In);
            metadata.Bind(this.Target, targetArgument);

            metadata.SetArgumentsCollection(new Collection<RuntimeArgument> { targetArgument });

            metadata.SetImplementationVariablesCollection(new Collection<Variable> { this.toCompensateToken });

            Fx.Assert(this.Body != null, "Body must be valid");
            metadata.SetImplementationChildrenCollection(new Collection<Activity> { this.Body });
        }

        protected override void Execute(NativeActivityContext context)
        {
            InternalExecute(context, null);
        }

        private void InternalExecute(NativeActivityContext context, ActivityInstance completedInstance)
        {
            var compensationExtension = context.GetExtension<CompensationExtension>();
            if (compensationExtension == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CompensateWithoutCompensableActivity(this.DisplayName)));
            }

            var token = Target.Get(context);
            var tokenData = token == null ? null : compensationExtension.Get(token.CompensationId);

            Fx.Assert(tokenData != null, "CompensationTokenData must be valid");

            if (tokenData.ExecutionTracker.Count > 0)
            {
                if (this.onChildCompensated == null)
                {
                    this.onChildCompensated = new CompletionCallback(InternalExecute);
                }

                this.toCompensateToken.Set(context, new CompensationToken(tokenData.ExecutionTracker.Get()));

                Fx.Assert(Body != null, "Body must be valid");
                context.ScheduleActivity(Body, this.onChildCompensated);
            }     
        }

        protected override void Cancel(NativeActivityContext context)
        {
            // Suppress Cancel   
        }

    }

}
