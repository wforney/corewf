// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Collections.Generic;

    public abstract partial class Activity
    {
        internal partial class ReflectedInformation
        {
            /// <summary>
            /// The DictionaryArgumentHelper class.
            /// Implements the <see cref="DictionaryArgumentHelper" />
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <seealso cref="DictionaryArgumentHelper" />
            private class DictionaryArgumentHelper<T> : DictionaryArgumentHelper where T : Argument
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="DictionaryArgumentHelper{T}" /> class.
                /// </summary>
                /// <param name="propertyValue">The property value.</param>
                /// <param name="propertyName">Name of the property.</param>
                public DictionaryArgumentHelper(object propertyValue, string propertyName)
                    : base()
                {
                    var argumentDictionary = propertyValue as IEnumerable<KeyValuePair<string, T>>;

                    this.RuntimeArguments = GetRuntimeArguments(argumentDictionary, propertyName);
                }
            }
        }
    }
}