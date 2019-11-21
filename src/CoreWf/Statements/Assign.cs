// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using System.Activities;
    using System.Activities.Runtime;
    using System;
    using System.Collections.ObjectModel;
    using System.ComponentModel;

    public sealed class Assign : CodeActivity
    {
        public Assign()
            : base()
        {
        }

        [RequiredArgument]
        [DefaultValue(null)]
        public OutArgument To
        {
            get;
            set;
        }

        [RequiredArgument]
        [DefaultValue(null)]
        public InArgument Value
        {
            get;
            set;
        }

        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            var arguments = new Collection<RuntimeArgument>();
            
            var valueType = TypeHelper.ObjectType;

            if (this.Value != null)
            {
                valueType = this.Value.ArgumentType;
            }

            var valueArgument = new RuntimeArgument("Value", valueType, ArgumentDirection.In, true);
            metadata.Bind(this.Value, valueArgument);

            var toType = TypeHelper.ObjectType;

            if (this.To != null)
            {
                toType = this.To.ArgumentType;
            }

            var toArgument = new RuntimeArgument("To", toType, ArgumentDirection.Out, true);
            metadata.Bind(this.To, toArgument);

            arguments.Add(valueArgument);
            arguments.Add(toArgument);

            metadata.SetArgumentsCollection(arguments);

            if (this.Value != null && this.To != null)
            {
                if (!TypeHelper.AreTypesCompatible(this.Value.ArgumentType, this.To.ArgumentType))
                {
                    metadata.AddValidationError(SR.TypeMismatchForAssign(
                                this.Value.ArgumentType,
                                this.To.ArgumentType,
                                this.DisplayName));
                }
            }
        }

        protected override void Execute(CodeActivityContext context)
        {
            this.To.Set(context, this.Value.Get(context));
        }
    }

    public sealed class Assign<T> : CodeActivity
    {
        public Assign()
            : base()
        {
        }

        [RequiredArgument]
        [DefaultValue(null)]
        public OutArgument<T> To
        {
            get;
            set;
        }

        [RequiredArgument]
        [DefaultValue(null)]
        public InArgument<T> Value
        {
            get;
            set;
        }

        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            var arguments = new Collection<RuntimeArgument>();

            var valueArgument = new RuntimeArgument("Value", typeof(T), ArgumentDirection.In, true);
            metadata.Bind(this.Value, valueArgument);

            var toArgument = new RuntimeArgument("To", typeof(T), ArgumentDirection.Out, true);
            metadata.Bind(this.To, toArgument);

            arguments.Add(valueArgument);
            arguments.Add(toArgument);

            metadata.SetArgumentsCollection(arguments);
        }

        protected override void Execute(CodeActivityContext context)
        {
            context.SetValue(this.To, this.Value.Get(context));
        }
    }
}
