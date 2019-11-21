// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Collections;

    internal static partial class ActivityUtilities
    {
        private static class ArgumentTypeDefinitionsCache
        {
            private static readonly Hashtable inArgumentTypeDefinitions = new Hashtable();
            private static readonly Hashtable inOutArgumentTypeDefinitions = new Hashtable();
            private static readonly Hashtable outArgumentTypeDefinitions = new Hashtable();

            public static Type GetArgumentType(Type type, ArgumentDirection direction)
            {
                Hashtable lookupTable;
                if (direction == ArgumentDirection.In)
                {
                    lookupTable = inArgumentTypeDefinitions;
                }
                else if (direction == ArgumentDirection.Out)
                {
                    lookupTable = outArgumentTypeDefinitions;
                }
                else
                {
                    lookupTable = inOutArgumentTypeDefinitions;
                }

                var argumentType = lookupTable[type] as Type;
                if (argumentType == null)
                {
                    argumentType = CreateArgumentType(type, direction);
                    lock (lookupTable)
                    {
                        lookupTable[type] = argumentType;
                    }
                }

                return argumentType;
            }

            private static Type CreateArgumentType(Type type, ArgumentDirection direction)
            {
                Type argumentType;
                if (direction == ArgumentDirection.In)
                {
                    argumentType = ActivityUtilities.inArgumentGenericType.MakeGenericType(type);
                }
                else if (direction == ArgumentDirection.Out)
                {
                    argumentType = ActivityUtilities.outArgumentGenericType.MakeGenericType(type);
                }
                else
                {
                    argumentType = ActivityUtilities.inOutArgumentGenericType.MakeGenericType(type);
                }

                return argumentType;
            }
        }
    }
}
