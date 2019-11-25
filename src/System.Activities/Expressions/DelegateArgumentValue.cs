// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Expressions
{
    using Portable.Xaml.Markup;

    using System.Activities.Runtime;

    [ContentProperty("DelegateArgument")]
    public sealed class DelegateArgumentValue<T> : EnvironmentLocationValue<T>
    {
        public DelegateArgumentValue()
            : base()
        {
        }

        public DelegateArgumentValue(DelegateArgument delegateArgument)
            : this()
        {
            this.DelegateArgument = delegateArgument;
        }

        public DelegateArgument DelegateArgument
        {
            get;
            set;
        }

        public override LocationReference LocationReference
        {
            get { return this.DelegateArgument; }
        }

        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            if (this.DelegateArgument == null)
            {
                metadata.AddValidationError(SR.DelegateArgumentMustBeSet);
            }
            else
            {
                if (!this.DelegateArgument.IsInTree)
                {
                    metadata.AddValidationError(SR.DelegateArgumentMustBeReferenced(this.DelegateArgument.Name));
                }

                if (!metadata.Environment.IsVisible(this.DelegateArgument))
                {
                    metadata.AddValidationError(SR.DelegateArgumentNotVisible(this.DelegateArgument.Name));
                }

                if (!(this.DelegateArgument is DelegateInArgument<T>) && !TypeHelper.AreTypesCompatible(this.DelegateArgument.Type, typeof(T)))
                {
                    metadata.AddValidationError(SR.DelegateArgumentTypeInvalid(this.DelegateArgument, typeof(T), this.DelegateArgument.Type));
                }
            }
        }
    }
}
