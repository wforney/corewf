// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Internals
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Reflection;

    /// <summary>
    /// The ReflectionExtensions class.
    /// </summary>
    internal static class ReflectionExtensions
    {
        #region Type

        /// <summary>
        /// Assemblies the specified type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Assembly.</returns>
        public static Assembly Assembly(this Type type) => type.GetTypeInfo().Assembly;

        /// <summary>
        /// Bases the type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Type.</returns>
        public static Type BaseType(this Type type) => type.GetTypeInfo().BaseType;

        /// <summary>
        /// Determines whether [contains generic parameters] [the specified type].
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if [contains generic parameters] [the specified type]; otherwise, <c>false</c>.</returns>
        public static bool ContainsGenericParameters(this Type type) => type.GetTypeInfo().ContainsGenericParameters;

        /// <summary>
        /// Gets the constructor.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="types">The types.</param>
        /// <returns>ConstructorInfo.</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        public static ConstructorInfo GetConstructor(this Type type, Type[] types) => throw new PlatformNotSupportedException();

        /// <summary>
        /// Gets the constructor.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="bindingAttr">The binding attribute.</param>
        /// <param name="binder">The binder.</param>
        /// <param name="types">The types.</param>
        /// <param name="modifiers">The modifiers.</param>
        /// <returns>ConstructorInfo.</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        public static ConstructorInfo GetConstructor(this Type type, BindingFlags bindingAttr, object binder, Type[] types, object[] modifiers) =>
            throw new PlatformNotSupportedException();

        /// <summary>
        /// Gets the property.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="name">The name.</param>
        /// <param name="bindingAttr">The binding attribute.</param>
        /// <returns>PropertyInfo.</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        public static PropertyInfo GetProperty(this Type type, string name, BindingFlags bindingAttr) => throw new PlatformNotSupportedException();

        /// <summary>
        /// Gets the method.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="methodName">Name of the method.</param>
        /// <param name="bindingFlags">The binding flags.</param>
        /// <param name="parameterTypes">The parameter types.</param>
        /// <param name="genericTypeArguments">The generic type arguments.</param>
        /// <returns>MethodInfo.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "<Pending>")]
        public static MethodInfo GetMethod(this Type type, string methodName, BindingFlags bindingFlags, Type[] parameterTypes, Type[] genericTypeArguments = null)
        {
            MethodInfo match = null;
            var methods = type.GetMethods(bindingFlags);
            foreach (var method in methods)
            {
                var methodToMatch = method;
                if (!string.Equals(methodToMatch.Name, methodName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // If there are genericTypeArguments, see if the method is generic and use the parameterInfo[]
                // for the method returned by MakeGenericMethod.
                if ((genericTypeArguments != null) && (genericTypeArguments.Length > 0))
                {
                    if (!methodToMatch.ContainsGenericParameters || (methodToMatch.GetGenericArguments().Length != genericTypeArguments.Length))
                    {
                        // not a match
                        continue;
                    }
                    try
                    {
                        methodToMatch = methodToMatch.MakeGenericMethod(genericTypeArguments);
                        if (methodToMatch == null)
                        {
                            continue;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Constraint violations will throw this exception--don't add to candidates
                        continue;
                    }
                }

                var methodParameters = methodToMatch.GetParameters();
                if (ParametersMatch(methodParameters, parameterTypes))
                {
                    match = methodToMatch;
                    break;
                }
                else
                {
                    continue;
                }
            }

            return match;
        }

        /// <summary>
        /// Determines whether the specified type is abstract.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if the specified type is abstract; otherwise, <c>false</c>.</returns>
        public static bool IsAbstract(this Type type) => type.GetTypeInfo().IsAbstract;

        //public static bool IsAssignableFrom(this Type type, Type otherType)
        //{
        //    return type.GetTypeInfo().IsAssignableFrom(otherType.GetTypeInfo());
        //}
        /// <summary>
        /// Determines whether the specified type is class.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if the specified type is class; otherwise, <c>false</c>.</returns>
        public static bool IsClass(this Type type) => type.GetTypeInfo().IsClass;

        /// <summary>
        /// Determines whether the specified attribute type is defined.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="attributeType">Type of the attribute.</param>
        /// <param name="inherit">if set to <c>true</c> [inherit].</param>
        /// <returns><c>true</c> if the specified attribute type is defined; otherwise, <c>false</c>.</returns>
        public static bool IsDefined(this Type type, Type attributeType, bool inherit) => type.GetTypeInfo().IsDefined(attributeType, inherit);

        /// <summary>
        /// Determines whether the specified type is enum.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if the specified type is enum; otherwise, <c>false</c>.</returns>
        public static bool IsEnum(this Type type) => type.GetTypeInfo().IsEnum;

        /// <summary>
        /// Determines whether [is generic type] [the specified type].
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if [is generic type] [the specified type]; otherwise, <c>false</c>.</returns>
        public static bool IsGenericType(this Type type) => type.GetTypeInfo().IsGenericType;

        /// <summary>
        /// Determines whether the specified type is interface.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if the specified type is interface; otherwise, <c>false</c>.</returns>
        public static bool IsInterface(this Type type) => type.GetTypeInfo().IsInterface;

        /// <summary>
        /// Determines whether [is instance of type] [the specified o].
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="o">The o.</param>
        /// <returns><c>true</c> if [is instance of type] [the specified o]; otherwise, <c>false</c>.</returns>
        public static bool IsInstanceOfType(this Type type, object o) => o == null ? false : type.GetTypeInfo().IsAssignableFrom(o.GetType().GetTypeInfo());

        /// <summary>
        /// Determines whether [is marshal by reference] [the specified type].
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if [is marshal by reference] [the specified type]; otherwise, <c>false</c>.</returns>
        public static bool IsMarshalByRef(this Type type) => type.GetTypeInfo().IsMarshalByRef;

        /// <summary>
        /// Determines whether [is not public] [the specified type].
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if [is not public] [the specified type]; otherwise, <c>false</c>.</returns>
        public static bool IsNotPublic(this Type type) => type.GetTypeInfo().IsNotPublic;

        /// <summary>
        /// Determines whether the specified type is sealed.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if the specified type is sealed; otherwise, <c>false</c>.</returns>
        public static bool IsSealed(this Type type) => type.GetTypeInfo().IsSealed;

        /// <summary>
        /// Determines whether [is value type] [the specified type].
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if [is value type] [the specified type]; otherwise, <c>false</c>.</returns>
        public static bool IsValueType(this Type type) => type.GetTypeInfo().IsValueType;

        /// <summary>
        /// Gets the interface map.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="interfaceType">Type of the interface.</param>
        /// <returns>InterfaceMapping.</returns>
        public static InterfaceMapping GetInterfaceMap(this Type type, Type interfaceType) => type.GetTypeInfo().GetRuntimeInterfaceMap(interfaceType);

        /// <summary>
        /// Gets the member.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="name">The name.</param>
        /// <param name="bindingAttr">The binding attribute.</param>
        /// <returns>MemberInfo[].</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        public static MemberInfo[] GetMember(this Type type, string name, BindingFlags bindingAttr) => throw new PlatformNotSupportedException();

        /// <summary>
        /// Gets the members.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="bindingAttr">The binding attribute.</param>
        /// <returns>MemberInfo[].</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        public static MemberInfo[] GetMembers(this Type type, BindingFlags bindingAttr) => throw new PlatformNotSupportedException();

        // TypeCode does not exist in N, but it is used by ServiceModel.
        // This extension method was copied from System.Private.PortableThunks\Internal\PortableLibraryThunks\System\TypeThunks.cs
        /// <summary>
        /// Gets the type code.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>TypeCode.</returns>
        public static TypeCode GetTypeCode(this Type type)
        {
            if (type == null)
            {
                return TypeCode.Empty;
            }

            if (type == typeof(bool))
            {
                return TypeCode.Boolean;
            }

            if (type == typeof(char))
            {
                return TypeCode.Char;
            }

            if (type == typeof(sbyte))
            {
                return TypeCode.SByte;
            }

            if (type == typeof(byte))
            {
                return TypeCode.Byte;
            }

            if (type == typeof(short))
            {
                return TypeCode.Int16;
            }

            if (type == typeof(ushort))
            {
                return TypeCode.UInt16;
            }

            if (type == typeof(int))
            {
                return TypeCode.Int32;
            }

            if (type == typeof(uint))
            {
                return TypeCode.UInt32;
            }

            if (type == typeof(long))
            {
                return TypeCode.Int64;
            }

            if (type == typeof(ulong))
            {
                return TypeCode.UInt64;
            }

            if (type == typeof(float))
            {
                return TypeCode.Single;
            }

            if (type == typeof(double))
            {
                return TypeCode.Double;
            }

            if (type == typeof(decimal))
            {
                return TypeCode.Decimal;
            }

            if (type == typeof(DateTime))
            {
                return TypeCode.DateTime;
            }

            if (type == typeof(string))
            {
                return TypeCode.String;
            }

            if (type.GetTypeInfo().IsEnum)
            {
                return GetTypeCode(Enum.GetUnderlyingType(type));
            }

            return TypeCode.Object;
        }

        #endregion Type

        #region ConstructorInfo

        /// <summary>
        /// Determines whether the specified ci is public.
        /// </summary>
        /// <param name="ci">The ci.</param>
        /// <returns><c>true</c> if the specified ci is public; otherwise, <c>false</c>.</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        public static bool IsPublic(this ConstructorInfo ci) => throw new PlatformNotSupportedException();

        /// <summary>
        /// Invokes the specified invoke attribute.
        /// </summary>
        /// <param name="ci">The ci.</param>
        /// <param name="invokeAttr">The invoke attribute.</param>
        /// <param name="binder">The binder.</param>
        /// <param name="parameters">The parameters.</param>
        /// <param name="culture">The culture.</param>
        /// <returns>System.Object.</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        public static object Invoke(this ConstructorInfo ci, BindingFlags invokeAttr, object binder, object[] parameters, CultureInfo culture) =>
            throw new PlatformNotSupportedException();

        #endregion ConstructorInfo

        #region MethodInfo, MethodBase

        /// <summary>
        /// Methods the handle.
        /// </summary>
        /// <param name="mb">The mb.</param>
        /// <returns>RuntimeMethodHandle.</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        public static RuntimeMethodHandle MethodHandle(this MethodBase mb) => throw new PlatformNotSupportedException();

        /// <summary>
        /// Methods the handle.
        /// </summary>
        /// <param name="mi">The mi.</param>
        /// <returns>RuntimeMethodHandle.</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        public static RuntimeMethodHandle MethodHandle(this MethodInfo mi) => throw new PlatformNotSupportedException();

        /// <summary>
        /// Reflecteds the type.
        /// </summary>
        /// <param name="mi">The mi.</param>
        /// <returns>Type.</returns>
        /// <exception cref="PlatformNotSupportedException"></exception>
        public static Type ReflectedType(this MethodInfo mi) => throw new PlatformNotSupportedException();

        #endregion MethodInfo, MethodBase

        #region HelperMethods

        /// <summary>
        /// If the ParameterInfo represents a "params" array, return the Type of that params array.
        /// Otherwise, return null.
        /// </summary>
        /// <param name="parameterInfo">The parameter information.</param>
        /// <returns>Type.</returns>
        private static Type ParamArrayType(ParameterInfo parameterInfo)
        {
            foreach (var customAttribute in parameterInfo.CustomAttributes)
            {
                if (customAttribute.AttributeType == typeof(ParamArrayAttribute))
                {
                    return parameterInfo.ParameterType.GetElementType();
                }
            }

            return null;
        }

        /// <summary>
        /// Returns true if the type of the ParameterInfo matches parameterType or
        /// if parameterType is null and the ParameterInfo has a default value (optional parameter)
        /// </summary>
        /// <param name="parameterInfo">The parameter information.</param>
        /// <param name="parameterType">Type of the parameter.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private static bool ParameterTypeMatch(ParameterInfo parameterInfo, Type parameterType) =>
            parameterInfo.ParameterType == parameterType || (parameterType == null) && parameterInfo.HasDefaultValue;

        /// <summary>
        /// Returns true if the last formal parameter (parameterInfos) matches the last of the parameterTypes,
        /// taking into account the possibility that the last formal parameter is a "params" array.
        /// </summary>
        /// <param name="parameterInfos">The parameter infos.</param>
        /// <param name="parameterTypes">The parameter types.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private static bool LastParameterInfoMatchesRemainingParameters(ParameterInfo[] parameterInfos, Type[] parameterTypes)
        {
            // The last parameter might NOT be a "params" array.
            if (parameterInfos[^1].ParameterType == parameterTypes[parameterInfos.Length - 1])
            {
                return true;
            }

            var paramArrayType = ParamArrayType(parameterInfos[^1]);
            if (null == paramArrayType)
            {
                return false;
            }

            for (var i = parameterInfos.Length - 1; i < parameterTypes.Length; i++)
            {
                if (parameterTypes[i] != paramArrayType)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true if the specified ParameterInfo[] (formal parameters)
        /// matches the specified Type[] (actual parameters).
        /// Takes into account optional parameters (with default values) and the last
        /// formal parameter being a "params" array.
        /// </summary>
        /// <param name="parameterInfos">The parameter infos.</param>
        /// <param name="parameterTypes">The parameter types.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private static bool ParametersMatch(ParameterInfo[] parameterInfos, Type[] parameterTypes)
        {
            // Most common case first - matching number of parameters.
            if (parameterInfos.Length == parameterTypes.Length)
            {
                // Special case = no parameters
                if (parameterInfos.Length == 0)
                {
                    return true;
                }

                // Check all but the last parameter. We will check the last parameter next as
                // a special case to check for a "params" array.
                for (var i = 0; i < parameterInfos.Length - 1; i++)
                {
                    if (!ParameterTypeMatch(parameterInfos[i], parameterTypes[i]))
                    {
                        return false;
                    }
                }

                // Now we need to check the last parameter. It might be a ParamArray, so we need to deal with that.
                if (!LastParameterInfoMatchesRemainingParameters(parameterInfos, parameterTypes))
                {
                    return false;
                }

                // The last parameter matches the ParamArray type
                return true;
            }

            // If the number of parameterTypes is LESS than the number of parameterInfos, then all the
            // types must match and the missing parameterInfos must have default values.
            if (parameterTypes.Length < parameterInfos.Length)
            {
                int i;
                for (i = 0; i < parameterTypes.Length; i++)
                {
                    if (parameterInfos[i].ParameterType != parameterTypes[i])
                    {
                        return false;
                    }
                }

                for (var j = i; j < parameterInfos.Length; j++)
                {
                    if (parameterInfos[j].HasDefaultValue)
                    {
                        continue;
                    }

                    // Only the last parameter is allowed to be a ParamArray.
                    if ((j < parameterInfos.Length - 1) || (null == ParamArrayType(parameterInfos[j])))
                    {
                        return false;
                    }
                }

                // if we get here, not all parameters were specified, but the missing ones have
                // default values or the last one is a ParamArray, so we have a match.
                return true;
            }

            // If we get here, the number of actual parameters is GREATER than the number of formal parameters.

            // If there are no formal parameters, we have no match.
            if (parameterInfos.Length == 0)
            {
                return false;
            }

            // Check all but the last parameter. We will check the last parameter next.
            for (var i = 0; i < parameterInfos.Length - 1; i++)
            {
                if (!ParameterTypeMatch(parameterInfos[i], parameterTypes[i]))
                {
                    return false;
                }
            }

            // Now we need to check the last formal parameter against the remaining actual parameters. It might be a ParamArray, so we need to deal with that.
            if (!LastParameterInfoMatchesRemainingParameters(parameterInfos, parameterTypes))
            {
                return false;
            }

            // The remaining parameters match the ParamArray type
            return true;
        }

        #endregion HelperMethods
    }
}