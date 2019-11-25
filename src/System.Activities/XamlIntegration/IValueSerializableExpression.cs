// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.XamlIntegration
{
    using Portable.Xaml.Markup;

    public interface IValueSerializableExpression
    {
        bool CanConvertToString(IValueSerializerContext context);

        string ConvertToString(IValueSerializerContext context);
    }
}
