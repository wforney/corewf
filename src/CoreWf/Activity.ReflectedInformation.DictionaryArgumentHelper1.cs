// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Activities.Internals;
    using System.Collections.Generic;

    public abstract partial class Activity
    {

internal partial class ReflectedInformation
        {
            /// <summary>
            /// The DictionaryArgumentHelper class.
            /// </summary>
            private class DictionaryArgumentHelper
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="DictionaryArgumentHelper" /> class.
                /// </summary>
                protected DictionaryArgumentHelper()
                {
                }

                /// <summary>
                /// Gets or sets the runtime arguments.
                /// </summary>
                /// <value>The runtime arguments.</value>
                public IList<RuntimeArgument> RuntimeArguments
                {
                    get;
                    protected set;
                }

                /// <summary>
                /// Tries the get runtime arguments.
                /// </summary>
                /// <param name="propertyValue">The property value.</param>
                /// <param name="propertyName">Name of the property.</param>
                /// <returns>IList&lt;RuntimeArgument&gt;.</returns>
                public static IList<RuntimeArgument> TryGetRuntimeArguments(object propertyValue, string propertyName)
                {
                    // special case each of the non-generic argument types to avoid reflection costs

                    if (propertyValue is IEnumerable<KeyValuePair<string, Argument>> argumentEnumerable)
                    {
                        return GetRuntimeArguments(argumentEnumerable, propertyName);
                    }

                    if (propertyValue is IEnumerable<KeyValuePair<string, InArgument>> inArgumentEnumerable)
                    {
                        return GetRuntimeArguments(inArgumentEnumerable, propertyName);
                    }

                    if (propertyValue is IEnumerable<KeyValuePair<string, OutArgument>> outArgumentEnumerable)
                    {
                        return GetRuntimeArguments(outArgumentEnumerable, propertyName);
                    }

                    if (propertyValue is IEnumerable<KeyValuePair<string, InOutArgument>> inOutArgumentEnumerable)
                    {
                        return GetRuntimeArguments(inOutArgumentEnumerable, propertyName);
                    }

                    return null;
                }

                /// <summary>
                /// Gets the runtime arguments.
                /// </summary>
                /// <typeparam name="T"></typeparam>
                /// <param name="argumentDictionary">The argument dictionary.</param>
                /// <param name="propertyName">Name of the property.</param>
                /// <returns>IList&lt;RuntimeArgument&gt;.</returns>
                /// <exception cref="ValidationException">
                /// </exception>
                protected static IList<RuntimeArgument> GetRuntimeArguments<T>(
                    IEnumerable<KeyValuePair<string, T>> argumentDictionary,
                    string propertyName)
                    where T : Argument
                {
                    IList<RuntimeArgument> runtimeArguments = new List<RuntimeArgument>();

                    foreach (var pair in argumentDictionary)
                    {
                        var key = pair.Key;
                        Argument value = pair.Value;

                        if (value == null)
                        {
                            var argName = key ?? "<null>";
                            throw FxTrace.Exception.AsError(new ValidationException(SR.MissingArgument(argName, propertyName)));
                        }

                        if (string.IsNullOrEmpty(key))
                        {
                            throw FxTrace.Exception.AsError(new ValidationException(SR.MissingNameProperty(value.ArgumentType)));
                        }

                        var runtimeArgument = new RuntimeArgument(key, value.ArgumentType, value.Direction, false, null, value);
                        runtimeArguments.Add(runtimeArgument);
                    }

                    return runtimeArguments;
                }
            }
        }
    }
}