// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Expressions;
    using System.Collections.Generic;
    using System.Reflection;

    internal static partial class ActivityUtilities
    {
        private static class LocationAccessExpressionTypeDefinitionsCache
        {
            private static readonly Dictionary<Type, ILocationReferenceExpression> environmentLocationReferenceTypeDefinitions = new Dictionary<Type, ILocationReferenceExpression>();
            private static readonly object environmentLocationReferenceTypeDefinitionsLock = new object();
            private static readonly Dictionary<Type, ILocationReferenceExpression> environmentLocationValueTypeDefinitions = new Dictionary<Type, ILocationReferenceExpression>();
            private static readonly object environmentLocationValueTypeDefinitionsLock = new object();
            private static readonly Dictionary<Type, ILocationReferenceExpression> locationReferenceValueTypeDefinitions = new Dictionary<Type, ILocationReferenceExpression>();
            private static readonly object locationReferenceValueTypeDefinitionsLock = new object();

            public static ActivityWithResult CreateNewLocationAccessExpression(Type type, bool isReference, bool useLocationReferenceValue, LocationReference locationReference)
            {
                Dictionary<Type, ILocationReferenceExpression> lookupTable;
                object tableLock;
                if (useLocationReferenceValue)
                {
                    lookupTable = locationReferenceValueTypeDefinitions;
                    tableLock = locationReferenceValueTypeDefinitionsLock;
                }
                else
                {
                    lookupTable = isReference ? environmentLocationReferenceTypeDefinitions : environmentLocationValueTypeDefinitions;
                    tableLock = isReference ? environmentLocationReferenceTypeDefinitionsLock : environmentLocationValueTypeDefinitionsLock;
                }

                ILocationReferenceExpression existingInstance;
                lock (tableLock)
                {
                    if (!lookupTable.TryGetValue(type, out existingInstance))
                    {
                        var locationAccessExpressionType = CreateLocationAccessExpressionType(type, isReference, useLocationReferenceValue);

                        // Create an "empty" (locationReference = null) instance to put in the
                        // cache. This empty instance will only be used to create other instances,
                        // including the instance returned from this method. The cached instance
                        // will never be included in an activity tree, so the cached instance's
                        // rootActivity field will not be filled in and thus will not pin all the
                        // objects in the activity tree. The cached empty instance has a null
                        // locationReference because locationReference also pins parts of activity tree.
                        existingInstance = (ILocationReferenceExpression)Activator.CreateInstance(
                            locationAccessExpressionType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { null }, null);

                        lookupTable[type] = existingInstance;
                    }
                }

                return existingInstance.CreateNewInstance(locationReference);
            }

            private static Type CreateLocationAccessExpressionType(Type type, bool isReference, bool useLocationReferenceValue)
            {
                Type openType;
                if (useLocationReferenceValue)
                {
                    openType = locationReferenceValueType;
                }
                else
                {
                    openType = isReference ? environmentLocationReferenceType : environmentLocationValueType;
                }

                return openType.MakeGenericType(type);
            }
        }
    }
}
