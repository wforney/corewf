// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Validation
{
    using System;
    using System.Activities.Internals;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Windows.Markup;

    public abstract class Constraint : NativeActivity
    {
        public const string ValidationErrorListPropertyName = "System.Activities.Validation.Constraint.ValidationErrorList";

        internal const string ToValidateArgumentName = "ToValidate";
        internal const string ValidationErrorListArgumentName = "ViolationList";
        internal const string ToValidateContextArgumentName = "ToValidateContext";
        private readonly RuntimeArgument toValidate;
        private readonly RuntimeArgument violationList;
        private readonly RuntimeArgument toValidateContext;

        internal Constraint()
        {
            this.toValidate = new RuntimeArgument(ToValidateArgumentName, typeof(object), ArgumentDirection.In);
            this.toValidateContext = new RuntimeArgument(ToValidateContextArgumentName, typeof(ValidationContext), ArgumentDirection.In); 
            this.violationList = new RuntimeArgument(ValidationErrorListArgumentName, typeof(IList<ValidationError>), ArgumentDirection.Out);
        }

        public static void AddValidationError(NativeActivityContext context, ValidationError error)
        {

            if (!(context.Properties.Find(ValidationErrorListPropertyName) is List<ValidationError> validationErrorList))
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.AddValidationErrorMustBeCalledFromConstraint(typeof(Constraint).Name)));
            }

            validationErrorList.Add(error);
        }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            metadata.SetArgumentsCollection(
                new Collection<RuntimeArgument>
                {
                    this.toValidate,
                    this.violationList,
                    this.toValidateContext
                });
        }

        protected override void Execute(NativeActivityContext context)
        {
            var objectToValidate = this.toValidate.Get<object>(context);
            var objectToValidateContext = this.toValidateContext.Get<ValidationContext>(context);

            if (objectToValidate == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotValidateNullObject(typeof(Constraint).Name, this.DisplayName)));
            }

            if (objectToValidateContext == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.ValidationContextCannotBeNull(typeof(Constraint).Name, this.DisplayName)));
            }

            var validationErrorList = new List<ValidationError>(1);
            context.Properties.Add(ValidationErrorListPropertyName, validationErrorList);

            this.violationList.Set(context, validationErrorList);

            OnExecute(context, objectToValidate, objectToValidateContext);
        }

        //[SuppressMessage(FxCop.Category.Naming, FxCop.Rule.IdentifiersShouldNotContainTypeNames,
        //    Justification = "Can't replace object with Object because of casing rules")]
        protected abstract void OnExecute(NativeActivityContext context, object objectToValidate, ValidationContext objectToValidateContext);
    }

    [ContentProperty("Body")]
    public sealed class Constraint<T> : Constraint
    {
        public Constraint()
        {
        }

        public ActivityAction<T, ValidationContext> Body
        {
            get;
            set;
        }
        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);

            if (this.Body != null)
            {
                metadata.SetDelegatesCollection(new Collection<ActivityDelegate> { this.Body });
            }
        }

        protected override void OnExecute(NativeActivityContext context, object objectToValidate, ValidationContext objectToValidateContext)
        {
            if (this.Body != null)
            {
                context.ScheduleAction(this.Body, (T)objectToValidate, objectToValidateContext);
            }
        }
    }
}
