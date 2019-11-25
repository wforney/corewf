// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{
    using System.Activities.Runtime;
    using System.ComponentModel;
    using System.Reflection;
    using System.Runtime.Serialization;

    public sealed class ValueTypeFieldReference<TOperand, TResult> : CodeActivity<Location<TResult>>
    {
        private FieldInfo fieldInfo;

        public ValueTypeFieldReference()
            : base()
        {
        }

        [DefaultValue(null)]
        public string FieldName
        {
            get;
            set;
        }

        [DefaultValue(null)]
        public InOutArgument<TOperand> OperandLocation
        {
            get;
            set;
        }

        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            var isRequired = false;
            if (!typeof(TOperand).IsValueType)
            {
                metadata.AddValidationError(SR.TypeMustbeValueType(typeof(TOperand).Name));
            }
            if (typeof(TOperand).IsEnum)
            {
                metadata.AddValidationError(SR.TargetTypeCannotBeEnum(this.GetType().Name, this.DisplayName));
            }
            if (string.IsNullOrEmpty(this.FieldName))
            {
                metadata.AddValidationError(SR.ActivityPropertyMustBeSet("FieldName", this.DisplayName));
            }
            else
            {
                this.fieldInfo = typeof(TOperand).GetField(this.FieldName);
                isRequired = this.fieldInfo != null && !this.fieldInfo.IsStatic;
                if (this.fieldInfo == null)
                {
                    metadata.AddValidationError(SR.MemberNotFound(this.FieldName, typeof(TOperand).Name));
                }
                else if (this.fieldInfo.IsInitOnly)
                {
                    metadata.AddValidationError(SR.MemberIsReadOnly(this.FieldName, typeof(TOperand).Name));
                }
            }

            MemberExpressionHelper.AddOperandLocationArgument<TOperand>(metadata, this.OperandLocation, isRequired);
        }

        protected override Location<TResult> Execute(CodeActivityContext context)
        {
            var operandLocationValue = this.OperandLocation.GetLocation(context);
            Fx.Assert(operandLocationValue != null, "OperandLocation must not be null");
            Fx.Assert(this.fieldInfo != null, "fieldInfo must not be null.");
            return new FieldLocation(this.fieldInfo, operandLocationValue);
        }

        [DataContract]
        internal class FieldLocation : Location<TResult>
        {
            private FieldInfo fieldInfo;
            private Location<TOperand> ownerLocation;

            public FieldLocation(FieldInfo fieldInfo, Location<TOperand> ownerLocation)
                : base()
            {
                this.fieldInfo = fieldInfo;
                this.ownerLocation = ownerLocation;
            }

            public override TResult Value
            {
                get
                {
                    return (TResult)this.fieldInfo.GetValue(this.ownerLocation.Value);
                }
                set
                {
                    object copy = this.ownerLocation.Value;
                    this.fieldInfo.SetValue(copy, value);
                    this.ownerLocation.Value = (TOperand)copy;
                }
            }

            [DataMember(Name = "fieldInfo")]
            internal FieldInfo SerializedFieldInfo
            {
                get { return this.fieldInfo; }
                set { this.fieldInfo = value; }
            }

            [DataMember(EmitDefaultValue = false, Name = "ownerLocation")]
            internal Location<TOperand> SerializedOwnerLocation
            {
                get { return this.ownerLocation; }
                set { this.ownerLocation = value; }
            }
        }
    }
}
