// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{
    using System.Collections.ObjectModel;
    using System.Reflection;
    using System;

    internal static class IndexerHelper
    {
        public static void OnGetArguments<TItem>(Collection<InArgument> indices, OutArgument<Location<TItem>> result, CodeActivityMetadata metadata)
        {
            for (var i = 0; i < indices.Count; i++)
            {
                var indexArgument = new RuntimeArgument("Index" + i, indices[i].ArgumentType, ArgumentDirection.In, true);
                metadata.Bind(indices[i], indexArgument);
                metadata.AddArgument(indexArgument);
            }

            var resultArgument = new RuntimeArgument("Result", typeof(Location<TItem>), ArgumentDirection.Out);
            metadata.Bind(result, resultArgument);
            metadata.AddArgument(resultArgument);
        }
        public static void CacheMethod<TOperand, TItem>(Collection<InArgument> indices, ref MethodInfo getMethod, ref MethodInfo setMethod)
        {
            var getTypes = new Type[indices.Count];
            for (var i = 0; i < indices.Count; i++)
            {
                getTypes[i] = indices[i].ArgumentType;
            }

            getMethod = typeof(TOperand).GetMethod("get_Item", getTypes);
            if (getMethod != null && !getMethod.IsSpecialName)
            {
                getMethod = null;
            }

            var setTypes = new Type[indices.Count + 1];
            for (var i = 0; i < indices.Count; i++)
            {
                setTypes[i] = indices[i].ArgumentType;
            }
            setTypes[setTypes.Length - 1] = typeof(TItem);
            setMethod = typeof(TOperand).GetMethod("set_Item", setTypes);
            if (setMethod != null && !setMethod.IsSpecialName)
            {
                setMethod = null;
            }
        }

    }

}
