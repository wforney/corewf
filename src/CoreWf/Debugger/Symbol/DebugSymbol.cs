// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Debugger.Symbol
{
    using System;
    using System.Activities.Runtime;
    using System.Xaml;

    [Fx.Tag.XamlVisible(false)]
    public static class DebugSymbol
    {
        private static readonly Type attachingTypeName = typeof(DebugSymbol);

        //[SuppressMessage(FxCop.Category.Security, FxCop.Rule.DoNotDeclareReadOnlyMutableReferenceTypes)]
        public static readonly AttachableMemberIdentifier SymbolName = new AttachableMemberIdentifier(attachingTypeName, "Symbol");


        [Fx.Tag.InheritThrows(From = "SetProperty", FromDeclaringType = typeof(AttachablePropertyServices))]
        public static void SetSymbol(object instance, object value)
        {
            AttachablePropertyServices.SetProperty(instance, SymbolName, value);
        }

        [Fx.Tag.InheritThrows(From = "TryGetProperty", FromDeclaringType = typeof(AttachablePropertyServices))]
        public static object GetSymbol(object instance) =>
            AttachablePropertyServices.TryGetProperty(instance, SymbolName, out string value) ? value : string.Empty;
    }
}
