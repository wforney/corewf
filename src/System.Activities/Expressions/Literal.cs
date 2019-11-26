// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{

    using System;
    using System.Activities.Runtime;
    using System.Activities.XamlIntegration;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using System.Windows.Markup;

    [DebuggerStepThrough]
    [ContentProperty("Value")]
    public sealed class Literal<T> : CodeActivity<T>, IExpressionContainer, IValueSerializableExpression
    {
        private static Regex ExpressionEscapeRegex = new Regex(@"^(%*\[)");

        public Literal()
        {
            this.UseOldFastPath = true;
        }

        public Literal(T value)
            : this()
        {
            this.Value = value;
        }

        public T Value
        {
            get;
            set;
        }

        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            var literalType = typeof(T);

            if (!literalType.IsValueType && literalType != TypeHelper.StringType)
            {
                metadata.AddValidationError(SR.LiteralsMustBeValueTypesOrImmutableTypes(TypeHelper.StringType, literalType));
            }
        }

        protected override T Execute(CodeActivityContext context)
        {
            return this.Value;
        }

        public override string ToString()
        {
            return this.Value == null ? "null" : this.Value.ToString();
        }

        public bool CanConvertToString(IValueSerializerContext context)
        {
            Type typeArgument;
            Type valueType;
            TypeConverter converter;

            if (this.Value == null)
            {
                return true;
            }
            
            typeArgument = typeof(T);
            valueType = this.Value.GetType();

            if (valueType == TypeHelper.StringType)
            {
                var myValue = this.Value as string;
                if (string.IsNullOrEmpty(myValue))
                {
                    return false;
                }
            }          

            converter = TypeDescriptor.GetConverter(typeArgument);
            if (typeArgument == valueType &&
                converter != null && 
                converter.CanConvertTo(TypeHelper.StringType) && 
                converter.CanConvertFrom(TypeHelper.StringType))
            {               
                if (valueType == typeof(DateTime))
                {
                    var literalValue = (DateTime)(object)this.Value;
                    return IsShortTimeFormattingSafe(literalValue);
                }

                if (valueType == typeof(DateTimeOffset))
                {
                    var literalValue = (DateTimeOffset)(object)this.Value;
                    return IsShortTimeFormattingSafe(literalValue);
                }

                return true;
            }

            return false;
        }

        private static bool IsShortTimeFormattingSafe(DateTime literalValue)
        {
            if (literalValue.Second == 0 && literalValue.Millisecond == 0 && literalValue.Kind == DateTimeKind.Unspecified)
            {
                // Dev10's DateTime's string conversion lost seconds, milliseconds, the remaining ticks and DateTimeKind data.
                // In Dev11, DateTime is special-cased, and is expanded to the property element syntax under a certain condition,
                // so that all aspects of DateTime data are completely preserved after xaml roundtrip.

                var noLeftOverTicksDateTime = new DateTime(
                    literalValue.Year,
                    literalValue.Month,
                    literalValue.Day,
                    literalValue.Hour,
                    literalValue.Minute,
                    literalValue.Second,
                    literalValue.Millisecond,
                    literalValue.Kind);

                if (literalValue.Ticks == noLeftOverTicksDateTime.Ticks)
                {
                    // Dev10 DateTime string conversion does not preserve leftover ticks
                    return true;
                }
            }

            return false;
        }

        private static bool IsShortTimeFormattingSafe(DateTimeOffset literalValue)
        {
            // DateTimeOffset is similar to DateTime in how its Dev10 string conversion did not preserve seconds, milliseconds, the remaining ticks and DateTimeKind data.
            return IsShortTimeFormattingSafe(literalValue.DateTime);
        }
        
        //[SuppressMessage(FxCop.Category.Globalization, FxCop.Rule.SpecifyIFormatProvider,
        //    Justification = "we really do want the string as-is")]
        public string ConvertToString(IValueSerializerContext context)
        {
            Type typeArgument;
            Type valueType;
            TypeConverter converter;

            if (this.Value == null)
            {
                return "[Nothing]";
            }

            typeArgument = typeof(T);
            valueType = this.Value.GetType();
            converter = TypeDescriptor.GetConverter(typeArgument);
            
            Fx.Assert(typeArgument == valueType &&
                converter != null &&
                converter.CanConvertTo(TypeHelper.StringType) &&
                converter.CanConvertFrom(TypeHelper.StringType),
                "Literal target type T and the return type mismatch or something wrong with its typeConverter!");

            // handle a Literal<string> of "[...]" by inserting escape chararcter '%' at the front
            if (typeArgument == TypeHelper.StringType)
            {
                var originalString = Convert.ToString(this.Value);
                if (originalString.EndsWith("]", StringComparison.Ordinal) && ExpressionEscapeRegex.IsMatch(originalString))
                {
                    return "%" + originalString;
                }
            }
            return converter.ConvertToString(context, this.Value);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool ShouldSerializeValue()
        {
            return !object.Equals(this.Value, default(T));
        }
    }
}
