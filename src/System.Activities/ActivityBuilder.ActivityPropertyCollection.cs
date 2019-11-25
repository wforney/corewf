// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Activities.Debugger;
    using System.Collections.ObjectModel;

    public sealed partial class ActivityBuilder : IDebuggableWorkflowTree
    {
        /// <summary>
        /// The ActivityPropertyCollection class. Implements the <see
        /// cref="System.Collections.ObjectModel.KeyedCollection{System.String,
        /// System.Activities.DynamicActivityProperty}" />
        /// </summary>
        /// <seealso cref="System.Collections.ObjectModel.KeyedCollection{System.String, System.Activities.DynamicActivityProperty}" />
        private class ActivityPropertyCollection : KeyedCollection<string, DynamicActivityProperty>
        {
            /// <summary>
            /// When implemented in a derived class, extracts the key from the specified element.
            /// </summary>
            /// <param name="item">The element from which to extract the key.</param>
            /// <returns>The key for the specified element.</returns>
            protected override string GetKeyForItem(DynamicActivityProperty item) => item.Name;
        }
    }
}
