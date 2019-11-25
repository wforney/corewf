// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;

    public abstract partial class Activity
    {
        /// <summary>
        /// The ReflectedInformation class.
        /// </summary>
        internal partial class ReflectedInformation
        {
            /// <summary>
            /// The parent
            /// </summary>
            private readonly Activity parent;

            /// <summary>
            /// The arguments
            /// </summary>
            private readonly Collection<RuntimeArgument> arguments;

            /// <summary>
            /// The variables
            /// </summary>
            private readonly Collection<Variable> variables;

            /// <summary>
            /// The children
            /// </summary>
            private readonly Collection<Activity> children;

            /// <summary>
            /// The delegates
            /// </summary>
            private readonly Collection<ActivityDelegate> delegates;

            /// <summary>
            /// The dictionary argument helper type
            /// </summary>
            private static readonly Type DictionaryArgumentHelperType = typeof(DictionaryArgumentHelper<>);

            /// <summary>
            /// The overload group attribute type
            /// </summary>
            private static readonly Type OverloadGroupAttributeType = typeof(OverloadGroupAttribute);

            /// <summary>
            /// Initializes a new instance of the <see cref="ReflectedInformation" /> class.
            /// </summary>
            /// <param name="owner">The owner.</param>
            public ReflectedInformation(Activity owner)
                : this(owner, ReflectedType.All)
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="ReflectedInformation" /> class.
            /// </summary>
            /// <param name="activity">The activity.</param>
            /// <param name="reflectType">Type of the reflect.</param>
            private ReflectedInformation(Activity activity, ReflectedType reflectType)
            {
                this.parent = activity;

                // reflect over our activity and gather relevant pieces of the system so that the developer
                // doesn't need to worry about "zipping up" his model to the constructs necessary for the
                // runtime to function correctly
                foreach (PropertyDescriptor propertyDescriptor in TypeDescriptor.GetProperties(activity))
                {
                    if ((reflectType & ReflectedType.Argument) == ReflectedType.Argument &&
                        ActivityUtilities.TryGetArgumentDirectionAndType(propertyDescriptor.PropertyType, out var direction, out var argumentType))
                    {
                        // We only do our magic for generic argument types.  If the property is a non-generic
                        // argument type then that means the type of the RuntimeArgument should be based on
                        // the type of the argument bound to it.  The activity author is responsible for dealing
                        // with these dynamic typing cases.
                        if (propertyDescriptor.PropertyType.IsGenericType)
                        {
                            var isRequired = this.GetIsArgumentRequired(propertyDescriptor);
                            var overloadGroupNames = this.GetOverloadGroupNames(propertyDescriptor);
                            var argument = new RuntimeArgument(
                                propertyDescriptor.Name, argumentType, direction, isRequired, overloadGroupNames, propertyDescriptor, activity);
                            this.Add(ref this.arguments, argument);
                        }
                    }
                    else if ((reflectType & ReflectedType.Variable) == ReflectedType.Variable &&
                        ActivityUtilities.IsVariableType(propertyDescriptor.PropertyType))
                    {
                        if (propertyDescriptor.GetValue(activity) is Variable variable)
                        {
                            this.Add(ref this.variables, variable);
                        }
                    }
                    else if ((reflectType & ReflectedType.Child) == ReflectedType.Child &&
                        ActivityUtilities.IsActivityType(propertyDescriptor.PropertyType))
                    {
                        var workflowElement = propertyDescriptor.GetValue(activity) as Activity;
                        this.Add(ref this.children, workflowElement);
                    }
                    else if ((reflectType & ReflectedType.ActivityDelegate) == ReflectedType.ActivityDelegate &&
                        ActivityUtilities.IsActivityDelegateType(propertyDescriptor.PropertyType))
                    {
                        var activityDelegate = propertyDescriptor.GetValue(activity) as ActivityDelegate;
                        this.Add(ref this.delegates, activityDelegate);
                    }
                    else
                    {
                        Type innerType;
                        var foundMatch = false;
                        if ((reflectType & ReflectedType.Argument) == ReflectedType.Argument)
                        {
                            var property = propertyDescriptor.GetValue(activity);
                            if (property != null)
                            {
                                var runtimeArguments = DictionaryArgumentHelper.TryGetRuntimeArguments(property, propertyDescriptor.Name);
                                if (runtimeArguments == null)
                                {
                                    if (ActivityUtilities.IsArgumentDictionaryType(propertyDescriptor.PropertyType, out innerType))
                                    {
                                        var concreteHelperType = DictionaryArgumentHelperType.MakeGenericType(innerType);
                                        var helper = Activator.CreateInstance(
                                            concreteHelperType,
                                            new object[] { property, propertyDescriptor.Name }) as DictionaryArgumentHelper;
                                        this.AddCollection(ref this.arguments, helper.RuntimeArguments);
                                        foundMatch = true;
                                    }
                                }
                                else
                                {
                                    this.AddCollection(ref this.arguments, runtimeArguments);
                                    foundMatch = true;
                                }
                            }
                        }

                        if (!foundMatch && ActivityUtilities.IsKnownCollectionType(propertyDescriptor.PropertyType, out innerType))
                        {
                            if ((reflectType & ReflectedType.Variable) == ReflectedType.Variable &&
                                ActivityUtilities.IsVariableType(innerType))
                            {
                                var enumerable = propertyDescriptor.GetValue(activity) as IEnumerable;

                                this.AddCollection(ref this.variables, enumerable);
                            }
                            else if ((reflectType & ReflectedType.Child) == ReflectedType.Child &&
                                ActivityUtilities.IsActivityType(innerType, false))
                            {
                                var enumerable = propertyDescriptor.GetValue(activity) as IEnumerable;

                                this.AddCollection(ref this.children, enumerable);
                            }
                            else if ((reflectType & ReflectedType.ActivityDelegate) == ReflectedType.ActivityDelegate &&
                                ActivityUtilities.IsActivityDelegateType(innerType))
                            {
                                var enumerable = propertyDescriptor.GetValue(activity) as IEnumerable;

                                this.AddCollection(ref this.delegates, enumerable);
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Gets the arguments.
            /// </summary>
            /// <param name="parent">The parent.</param>
            /// <returns>Collection&lt;RuntimeArgument&gt;.</returns>
            public static Collection<RuntimeArgument> GetArguments(Activity parent)
            {
                Collection<RuntimeArgument> arguments = null;

                if (parent != null)
                {
                    arguments = new ReflectedInformation(parent, ReflectedType.Argument).GetArguments();
                }

                if (arguments == null)
                {
                    arguments = new Collection<RuntimeArgument>();
                }

                return arguments;
            }

            /// <summary>
            /// Gets the variables.
            /// </summary>
            /// <param name="parent">The parent.</param>
            /// <returns>Collection&lt;Variable&gt;.</returns>
            public static Collection<Variable> GetVariables(Activity parent)
            {
                Collection<Variable> variables = null;

                if (parent != null)
                {
                    variables = new ReflectedInformation(parent, ReflectedType.Variable).GetVariables();
                }

                if (variables == null)
                {
                    variables = new Collection<Variable>();
                }

                return variables;
            }

            /// <summary>
            /// Gets the children.
            /// </summary>
            /// <param name="parent">The parent.</param>
            /// <returns>Collection&lt;Activity&gt;.</returns>
            public static Collection<Activity> GetChildren(Activity parent)
            {
                Collection<Activity> children = null;

                if (parent != null)
                {
                    children = new ReflectedInformation(parent, ReflectedType.Child).GetChildren();
                }

                if (children == null)
                {
                    children = new Collection<Activity>();
                }

                return children;
            }

            /// <summary>
            /// Gets the delegates.
            /// </summary>
            /// <param name="parent">The parent.</param>
            /// <returns>Collection&lt;ActivityDelegate&gt;.</returns>
            public static Collection<ActivityDelegate> GetDelegates(Activity parent)
            {
                Collection<ActivityDelegate> delegates = null;

                if (parent != null)
                {
                    delegates = new ReflectedInformation(parent, ReflectedType.ActivityDelegate).GetDelegates();
                }

                if (delegates == null)
                {
                    delegates = new Collection<ActivityDelegate>();
                }

                return delegates;
            }

            /// <summary>
            /// Gets the arguments.
            /// </summary>
            /// <returns>Collection&lt;RuntimeArgument&gt;.</returns>
            public Collection<RuntimeArgument> GetArguments() => this.arguments;

            /// <summary>
            /// Gets the variables.
            /// </summary>
            /// <returns>Collection&lt;Variable&gt;.</returns>
            public Collection<Variable> GetVariables() => this.variables;

            /// <summary>
            /// Gets the children.
            /// </summary>
            /// <returns>Collection&lt;Activity&gt;.</returns>
            public Collection<Activity> GetChildren() => this.children;

            /// <summary>
            /// Gets the delegates.
            /// </summary>
            /// <returns>Collection&lt;ActivityDelegate&gt;.</returns>
            public Collection<ActivityDelegate> GetDelegates() => this.delegates;

            /// <summary>
            /// Adds the collection.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="list">The list.</param>
            /// <param name="enumerable">The enumerable.</param>
            private void AddCollection<T>(ref Collection<T> list, IEnumerable enumerable)
                where T : class
            {
                if (enumerable != null)
                {
                    foreach (var obj in enumerable)
                    {
                        if (obj != null && obj is T)
                        {
                            this.Add(ref list, (T)obj);
                        }
                    }
                }
            }

            /// <summary>
            /// Adds the specified list.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="list">The list.</param>
            /// <param name="data">The data.</param>
            private void Add<T>(ref Collection<T> list, T data)
            {
                if (data != null)
                {
                    if (list == null)
                    {
                        list = new Collection<T>();
                    }

                    list.Add(data);
                }
            }

            /// <summary>
            /// Gets the is argument required.
            /// </summary>
            /// <param name="propertyDescriptor">The property descriptor.</param>
            /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
            private bool GetIsArgumentRequired(PropertyDescriptor propertyDescriptor) => propertyDescriptor.Attributes[typeof(RequiredArgumentAttribute)] != null;

            /// <summary>
            /// Gets the overload group names.
            /// </summary>
            /// <param name="propertyDescriptor">The property descriptor.</param>
            /// <returns>List&lt;System.String&gt;.</returns>
            private List<string> GetOverloadGroupNames(PropertyDescriptor propertyDescriptor)
            {
                var overloadGroupNames = new List<string>(0);
                var propertyAttributes = propertyDescriptor.Attributes;
                for (var i = 0; i < propertyAttributes.Count; i++)
                {
                    var attribute = propertyAttributes[i];
                    if (OverloadGroupAttributeType.IsAssignableFrom(attribute.GetType()))
                    {
                        overloadGroupNames.Add(((OverloadGroupAttribute)attribute).GroupName);
                    }
                }

                return overloadGroupNames;
            }
        }
    }
}