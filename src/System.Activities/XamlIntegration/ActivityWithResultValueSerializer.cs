// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.XamlIntegration
{
    using Portable.Xaml;
    using Portable.Xaml.Markup;

    using System;
    using System.Activities.Internals;

    /// <summary>
    /// The ActivityWithResultValueSerializer class. This class cannot be inherited. Implements the
    /// <see cref="Portable.Xaml.Markup.ValueSerializer" />
    /// </summary>
    /// <seealso cref="Portable.Xaml.Markup.ValueSerializer" />
    public sealed class ActivityWithResultValueSerializer : ValueSerializer
    {
        private static ActivityWithResultValueSerializer? valueSerializer;

        /// <summary>
        /// Determines whether this instance [can convert to string] the specified value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="context">The context.</param>
        /// <returns>
        /// <c>true</c> if this instance [can convert to string] the specified value; otherwise, <c>false</c>.
        /// </returns>
        public override bool CanConvertToString(object? value, IValueSerializerContext context)
        {
            if (AttachablePropertyServices.GetAttachedPropertyCount(value) > 0)
            {
                return false;
            }
            else if (value != null &&
                value is IValueSerializableExpression &&
                ((IValueSerializableExpression)value).CanConvertToString(context))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Converts to string.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="context">The context.</param>
        /// <returns>System.String.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public override string ConvertToString(object? value, IValueSerializerContext context)
        {
            if (value is IValueSerializableExpression ivsExpr)
            {
                return ivsExpr.ConvertToString(context);
            }

            throw FxTrace.Exception.AsError(new InvalidOperationException(SR.CannotSerializeExpression(value?.GetType())));
        }

        internal static bool CanConvertToStringWrapper(object? value, IValueSerializerContext context)
        {
            if (valueSerializer == null)
            {
                valueSerializer = new ActivityWithResultValueSerializer();
            }

            return valueSerializer.CanConvertToString(value, context);
        }

        internal static string ConvertToStringWrapper(object? value, IValueSerializerContext context)
        {
            if (valueSerializer == null)
            {
                valueSerializer = new ActivityWithResultValueSerializer();
            }

            return valueSerializer.ConvertToString(value, context);
        }
    }
}
