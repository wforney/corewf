// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Validation
{
    using System.Collections.ObjectModel;
    using System.ComponentModel;

    public sealed class AddValidationError : NativeActivity
    {
        public AddValidationError()
        {
        }

        public InArgument<string> Message
        {
            get;
            set;
        }

        [DefaultValue(null)]
        public InArgument<bool> IsWarning
        {
            get;
            set;
        }

        [DefaultValue(null)]
        public InArgument<string> PropertyName
        {
            get;
            set;
        }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            var arguments = new Collection<RuntimeArgument>();

            var messageArgument = new RuntimeArgument("Message", typeof(string), ArgumentDirection.In);
            metadata.Bind(this.Message, messageArgument);
            arguments.Add(messageArgument);

            var isWarningArgument = new RuntimeArgument("IsWarning", typeof(bool), ArgumentDirection.In, false);
            metadata.Bind(this.IsWarning, isWarningArgument);
            arguments.Add(isWarningArgument);
            
            var propertyNameArgument = new RuntimeArgument("PropertyName", typeof(string), ArgumentDirection.In, false);
            metadata.Bind(this.PropertyName, propertyNameArgument);
            arguments.Add(propertyNameArgument);

            metadata.SetArgumentsCollection(arguments);
        }

        protected override void Execute(NativeActivityContext context)
        {
            var isWarning = false;
            var propertyName = string.Empty;
            var errorCode = string.Empty;
            
            if (this.IsWarning != null)
            {
                isWarning = this.IsWarning.Get(context);
            }
            
            if (this.PropertyName != null)
            {
                propertyName = this.PropertyName.Get(context);            
            }
            
            Constraint.AddValidationError(context, new ValidationError(this.Message.Get(context), isWarning, propertyName));
        }
    }
}
