// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Debugger.Symbol
{
    using System;
    using System.Activities.Runtime;
    using System.Xaml;

    /// <summary>
    /// The DebugSymbol class.
    /// </summary>
    [Fx.Tag.XamlVisible(false)]
    public static class DebugSymbol
    {
        /// <summary>
        /// The symbol name
        /// </summary>
        public static readonly AttachableMemberIdentifier SymbolName = 
            new AttachableMemberIdentifier(attachingTypeName, "Symbol");

        /// <summary>
        /// The attaching type name
        /// </summary>
        private static readonly Type attachingTypeName = typeof(DebugSymbol);

        /// <summary>
        /// Gets the symbol.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <returns>System.Object.</returns>
        [Fx.Tag.InheritThrows(From = "TryGetProperty", FromDeclaringType = typeof(AttachablePropertyServices))]
        public static object GetSymbol(object instance) =>
            AttachablePropertyServices.TryGetProperty(instance, SymbolName, out string value) ? value : string.Empty;

        /// <summary>
        /// Sets the symbol.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="value">The value.</param>
        [Fx.Tag.InheritThrows(From = "SetProperty", FromDeclaringType = typeof(AttachablePropertyServices))]
        public static void SetSymbol(object instance, object value) =>
            AttachablePropertyServices.SetProperty(instance, SymbolName, value);
    }
}
