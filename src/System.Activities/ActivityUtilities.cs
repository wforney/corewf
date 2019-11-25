// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Expressions;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Validation;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Globalization;
    using System.Text;

    internal static partial class ActivityUtilities
    {
        private static readonly Type activityDelegateType = typeof(ActivityDelegate);
        private static readonly Type activityGenericType = typeof(Activity<>);
        private static readonly Type activityType = typeof(Activity);
        private static readonly Type argumentReferenceGenericType = typeof(ArgumentReference<>);
        private static readonly Type argumentType = typeof(Argument);
        private static readonly Type argumentValueGenericType = typeof(ArgumentValue<>);
        private static readonly Type constraintType = typeof(Constraint);
        private static readonly Type delegateArgumentValueGenericType = typeof(DelegateArgumentValue<>);
        private static readonly Type delegateInArgumentGenericType = typeof(DelegateInArgument<>);
        private static readonly Type delegateInArgumentType = typeof(DelegateInArgument);
        private static readonly Type delegateOutArgumentGenericType = typeof(DelegateOutArgument<>);
        private static readonly Type delegateOutArgumentType = typeof(DelegateOutArgument);
        private static readonly Type environmentLocationReferenceType = typeof(EnvironmentLocationReference<>);
        private static readonly Type environmentLocationValueType = typeof(EnvironmentLocationValue<>);
        private static readonly Type handleType = typeof(Handle);
        private static readonly Type iDictionaryGenericType = typeof(IDictionary<,>);
        private static readonly Type inArgumentGenericType = typeof(InArgument<>);
        private static readonly Type inArgumentOfObjectType = typeof(InArgument<object>);
        private static readonly Type inArgumentType = typeof(InArgument);
        private static readonly Type inOutArgumentGenericType = typeof(InOutArgument<>);
        private static readonly Type inOutArgumentOfObjectType = typeof(InOutArgument<object>);
        private static readonly Type inOutArgumentType = typeof(InOutArgument);
        private static readonly Type locationGenericType = typeof(Location<>);
        private static readonly Type locationReferenceValueType = typeof(LocationReferenceValue<>);
        private static readonly Type outArgumentGenericType = typeof(OutArgument<>);
        private static readonly Type outArgumentOfObjectType = typeof(OutArgument<object>);
        private static readonly Type outArgumentType = typeof(OutArgument);
        private static readonly Pop popActivity = new Pop();
        private static readonly Type runtimeArgumentType = typeof(RuntimeArgument);
        private static readonly Type variableGenericType = typeof(Variable<>);
        private static readonly Type variableReferenceGenericType = typeof(VariableReference<>);
        private static readonly Type variableType = typeof(Variable);
        private static readonly Type variableValueGenericType = typeof(VariableValue<>);
        private static IList<Type>? collectionInterfaces;
        private static PropertyChangedEventArgs? propertyChangedEventArgs;

        public delegate void ProcessActivityCallback(ChildActivity childActivity, ActivityCallStack parentChain);

        /// <summary>
        /// Gets the empty parameters.
        /// </summary>
        /// <value>The empty parameters.</value>
        /// <remarks>
        /// Can't delay create this one because we use object.ReferenceEquals on it in WorkflowInstance
        /// </remarks>
        public static ReadOnlyDictionary<string, object> EmptyParameters { get; } = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(0));

        internal static PropertyChangedEventArgs ValuePropertyChangedEventArgs
        {
            get
            {
                if (propertyChangedEventArgs == null)
                {
                    propertyChangedEventArgs = new PropertyChangedEventArgs("Value");
                }
                return propertyChangedEventArgs;
            }
        }

        private static IList<Type> CollectionInterfaces
        {
            get
            {
                if (collectionInterfaces == null)
                {
                    collectionInterfaces = new List<Type>(2)
                        {
                            typeof(IList<>),
                            typeof(ICollection<>)
                        };
                }
                return collectionInterfaces;
            }
        }

        /// <summary>
        /// Adds the specified collection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection">The collection.</param>
        /// <param name="data">The data.</param>
        public static void Add<T>(ref Collection<T>? collection, T data)
        {
            if (data != null)
            {
                if (collection == null)
                {
                    collection = new Collection<T>();
                }

                collection.Add(data);
            }
        }

        /// <summary>
        /// Adds the specified list.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list">The list.</param>
        /// <param name="data">The data.</param>
        public static void Add<T>(ref IList<T>? list, T data)
        {
            if (data != null)
            {
                if (list == null)
                {
                    list = new List<T>();
                }

                list.Add(data);
            }
        }

        /// <summary>
        /// Caches the root metadata.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <param name="hostEnvironment">The host environment.</param>
        /// <param name="options">The options.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="validationErrors">The validation errors.</param>
        /// <exception cref="InvalidOperationException"></exception>
        /// <remarks>
        /// We explicitly call this CacheRootMetadata since it treats the provided activity as the
        /// root of the tree.
        /// </remarks>
        public static void CacheRootMetadata(
            Activity activity,
            LocationReferenceEnvironment hostEnvironment,
            ProcessActivityTreeOptions options,
            ProcessActivityCallback callback,
            ref IList<ValidationError> validationErrors)
        {
            if (TD.CacheRootMetadataStartIsEnabled())
            {
                TD.CacheRootMetadataStart(activity.DisplayName);
            }
            if (!ShouldShortcut(activity, options))
            {
                lock (activity.ThisLock)
                {
                    if (!ShouldShortcut(activity, options))
                    {
                        if (activity.HasBeenAssociatedWithAnInstance)
                        {
                            throw FxTrace.Exception.AsError(new InvalidOperationException(SR.RootActivityAlreadyAssociatedWithInstance(activity.DisplayName)));
                        }

                        activity.InitializeAsRoot(hostEnvironment);

                        ProcessActivityTreeCore(new ChildActivity(activity, true), null, options, callback, ref validationErrors);

                        // Regardless of where the violations came from we only want to set
                        // ourselves RuntimeReady if there are no errors and are fully cached.
                        if (!ActivityValidationServices.HasErrors(validationErrors) && options.IsRuntimeReadyOptions)
                        {
                            // We don't really support progressive caching at runtime so we only set
                            // ourselves as runtime ready if we cached the whole workflow and
                            // created empty bindings. In order to support progressive caching we
                            // need to deal with the following issues:
                            // * We need a mechanism for supporting activities which supply extensions
                            // * We need to understand when we haven't created empty bindings so that
                            // we can progressively create them
                            // * We need a mechanism for making sure that we've validated parent related
                            // constraints at all possible callsites
                            activity.SetRuntimeReady();
                        }
                    }
                }
            }
            if (TD.CacheRootMetadataStopIsEnabled())
            {
                TD.CacheRootMetadataStop(activity.DisplayName);
            }
        }

        /// <summary>
        /// Creates the activity with result.
        /// </summary>
        /// <param name="resultType">Type of the result.</param>
        /// <returns>Type.</returns>
        public static Type CreateActivityWithResult(Type resultType) => activityGenericType.MakeGenericType(resultType);

        /// <summary>
        /// Creates the argument.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="direction">The direction.</param>
        /// <returns>Argument.</returns>
        public static Argument CreateArgument(Type? type, ArgumentDirection? direction)
        {
            var argumentType = ArgumentTypeDefinitionsCache.GetArgumentType(type, direction);

            var argument = (Argument)Activator.CreateInstance(argumentType);

            return argument;
        }

        /// <summary>
        /// Creates the argument of object.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <returns>Argument.</returns>
        public static Argument CreateArgumentOfObject(ArgumentDirection direction)
        {
            Argument argument;
            switch (direction)
            {
                case ArgumentDirection.In:
                    argument = (Argument)Activator.CreateInstance(inArgumentOfObjectType);
                    break;

                case ArgumentDirection.Out:
                    argument = (Argument)Activator.CreateInstance(outArgumentOfObjectType);
                    break;

                case ArgumentDirection.InOut:
                default:
                    argument = (Argument)Activator.CreateInstance(inOutArgumentOfObjectType);
                    break;
            }

            return argument;
        }

        public static CompletionBookmark? CreateCompletionBookmark(CompletionCallback onCompleted, ActivityInstance owningInstance) =>
            onCompleted != null ? new CompletionBookmark(new ActivityCompletionCallbackWrapper(onCompleted, owningInstance)) : null;

        public static CompletionBookmark? CreateCompletionBookmark(DelegateCompletionCallback onCompleted, ActivityInstance owningInstance) =>
            onCompleted != null ? new CompletionBookmark(new DelegateCompletionCallbackWrapper(onCompleted, owningInstance)) : null;

        public static CompletionBookmark? CreateCompletionBookmark<TResult>(CompletionCallback<TResult> onCompleted, ActivityInstance owningInstance) =>
            onCompleted != null ? new CompletionBookmark(new FuncCompletionCallbackWrapper<TResult>(onCompleted, owningInstance)) : null;

        public static FaultBookmark? CreateFaultBookmark(FaultCallback onFaulted, ActivityInstance owningInstance) =>
            onFaulted != null ? new FaultBookmark(new FaultCallbackWrapper(onFaulted, owningInstance)) : null;

        public static Type CreateLocation(Type? locationType) =>
            locationGenericType.MakeGenericType(locationType);

        public static ActivityWithResult CreateLocationAccessExpression(LocationReference locationReference, bool isReference, bool useLocationReferenceValue) =>
            LocationAccessExpressionTypeDefinitionsCache.CreateNewLocationAccessExpression(
                locationReference.Type, isReference, useLocationReferenceValue, locationReference);

        public static Argument CreateReferenceArgument(Type? argumentType, ArgumentDirection direction, string referencedArgumentName)
        {
            var argument = Argument.Create(argumentType, direction);

            object argumentReference;
            if (direction == ArgumentDirection.In)
            {
                // If it is an In then we need an ArgumentValue<T>
                argumentReference = Activator.CreateInstance(argumentValueGenericType.MakeGenericType(argumentType), referencedArgumentName);
            }
            else
            {
                // If it is InOut or Out we need an ArgumentReference<T>
                argumentReference = Activator.CreateInstance(argumentReferenceGenericType.MakeGenericType(argumentType), referencedArgumentName);
            }

            argument.Expression = (ActivityWithResult)argumentReference;
            return argument;
        }

        public static Variable CreateVariable(string name, Type type, VariableModifiers modifiers)
        {
            var variableType = variableGenericType.MakeGenericType(type);
            var variable = (Variable)Activator.CreateInstance(variableType);
            variable.Name = name;
            variable.Modifiers = modifiers;

            return variable;
        }

        public static object CreateVariableReference(Variable variable)
        {
            var genericVariableReferenceType = variableReferenceGenericType.MakeGenericType(variable.Type);
            var variableReference = Activator.CreateInstance(genericVariableReferenceType);
            genericVariableReferenceType.GetProperty("Variable").SetValue(variableReference, variable, null);
            return variableReference;
        }

        /// <summary>
        /// Finds the argument.
        /// </summary>
        /// <param name="argumentName">Name of the argument.</param>
        /// <param name="argumentConsumer">The argument consumer.</param>
        /// <returns>RuntimeArgument.</returns>
        /// <remarks>
        /// The argumentConsumer is the activity that is attempting to reference the argument with
        /// argumentName. That means that argumentConsumer must be in the Implementation of an
        /// activity that defines an argument with argumentName.
        /// </remarks>
        public static RuntimeArgument? FindArgument(string argumentName, Activity argumentConsumer)
        {
            if (argumentConsumer.MemberOf != null && argumentConsumer.MemberOf.Owner != null)
            {
                var targetActivity = argumentConsumer.MemberOf.Owner;

                for (var i = 0; i < targetActivity.RuntimeArguments.Count; i++)
                {
                    var argument = targetActivity.RuntimeArguments[i];

                    if (argument.Name == argumentName)
                    {
                        return argument;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finishes the caching subtree.
        /// </summary>
        /// <param name="subtreeRoot">The subtree root.</param>
        /// <param name="parentChain">The parent chain.</param>
        /// <param name="options">The options.</param>
        /// <remarks>
        /// This API is only valid from ProcessActivityCallbacks. It will cache the rest of the
        /// subtree rooted at the provided activity allowing inspection of child metadata before the
        /// normal caching pass hits it.
        /// </remarks>
        public static void FinishCachingSubtree(ChildActivity subtreeRoot, ActivityCallStack parentChain, ProcessActivityTreeOptions options)
        {
            IList<ValidationError>? discardedValidationErrors = null;
            ProcessActivityTreeCore(subtreeRoot, parentChain, ProcessActivityTreeOptions.GetFinishCachingSubtreeOptions(options), new ProcessActivityCallback(NoOpCallback), ref discardedValidationErrors);
        }

        public static void FinishCachingSubtree(ChildActivity subtreeRoot, ActivityCallStack parentChain, ProcessActivityTreeOptions options, ProcessActivityCallback callback)
        {
            IList<ValidationError>? discardedValidationErrors = null;
            ProcessActivityTreeCore(subtreeRoot, parentChain, ProcessActivityTreeOptions.GetFinishCachingSubtreeOptions(options), callback, ref discardedValidationErrors);
        }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <returns>System.String.</returns>
        /// <exception cref="ArgumentNullException">source</exception>
        public static string GetDisplayName(object source)
        {
            Fx.Assert(source != null, "caller must verify");
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return GetDisplayName(source.GetType());
        }

        public static string GetTraceString(Bookmark bookmark) =>
            bookmark.IsNamed ? $"'{bookmark.Name}'" : string.Format(CultureInfo.InvariantCulture, "<Unnamed Id={0}>", bookmark.Id);

        public static string GetTraceString(BookmarkScope bookmarkScope) =>
            bookmarkScope == null
                ? "<None>"
                : bookmarkScope.IsInitialized
                    ? $"'{bookmarkScope.Id.ToString()}'"
                    : string.Format(CultureInfo.InvariantCulture, "<Uninitialized TemporaryId={0}>", bookmarkScope.TemporaryId);

        public static bool IsActivityDelegateType(Type propertyType) =>
            TypeHelper.AreTypesCompatible(propertyType, activityDelegateType);

        public static bool IsActivityType(Type propertyType) =>
            IsActivityType(propertyType, true);

        public static bool IsActivityType(Type propertyType, bool includeConstraints)
        {
            if (!TypeHelper.AreTypesCompatible(propertyType, activityType))
            {
                return false;
            }

            // sometimes (for reflection analysis of Activity properties) we don't want constraints to count
            return includeConstraints || !TypeHelper.AreTypesCompatible(propertyType, constraintType);
        }

        public static bool IsArgumentDictionaryType(Type type, out Type? innerType)
        {
            if (type.IsGenericType)
            {
                var implementsIDictionary = false;
                Type? dictionaryInterfaceType = null;

                if (type.GetGenericTypeDefinition() == iDictionaryGenericType)
                {
                    implementsIDictionary = true;
                    dictionaryInterfaceType = type;
                }
                else
                {
                    foreach (var interfaceType in type.GetInterfaces())
                    {
                        if (interfaceType.IsGenericType &&
                            interfaceType.GetGenericTypeDefinition() == iDictionaryGenericType)
                        {
                            implementsIDictionary = true;
                            dictionaryInterfaceType = interfaceType;
                            break;
                        }
                    }
                }

                if (implementsIDictionary == true)
                {
                    var genericArguments = dictionaryInterfaceType?.GetGenericArguments();
                    if (genericArguments?[0] == TypeHelper.StringType &&
                        IsArgumentType(genericArguments[1]))
                    {
                        innerType = genericArguments[1];
                        return true;
                    }
                }
            }

            innerType = null;
            return false;
        }

        public static bool IsArgumentType(Type propertyType) => TypeHelper.AreTypesCompatible(propertyType, argumentType);

        public static bool IsCompletedState(ActivityInstanceState state) => state != ActivityInstanceState.Executing;

        public static bool IsHandle(Type type) => handleType.IsAssignableFrom(type);

        public static bool IsInScope(ActivityInstance potentialChild, ActivityInstance scope)
        {
            if (scope == null)
            {
                // No scope means we're in scope
                return true;
            }

            var walker = potentialChild;

            while (walker != null && walker != scope)
            {
                walker = walker.Parent;
            }

            return walker != null;
        }

        public static bool IsKnownCollectionType(Type type, out Type? innerType)
        {
            if (type.IsGenericType)
            {
                if (type.IsInterface)
                {
                    var localInterface = type.GetGenericTypeDefinition();
                    foreach (var knownInterface in CollectionInterfaces)
                    {
                        if (localInterface == knownInterface)
                        {
                            var genericArguments = type.GetGenericArguments();
                            if (genericArguments.Length == 1)
                            {
                                innerType = genericArguments[0];
                                return true;
                            }
                        }
                    }
                }
                else
                {
                    // Ask the type whether or not it implements any known collections.
                    var interfaceTypes = type.GetInterfaces();
                    foreach (var interfaceType in interfaceTypes)
                    {
                        if (interfaceType.IsGenericType)
                        {
                            var localInterface = interfaceType.GetGenericTypeDefinition();

                            foreach (var knownInterface in CollectionInterfaces)
                            {
                                if (localInterface == knownInterface)
                                {
                                    var genericArguments = interfaceType.GetGenericArguments();
                                    if (genericArguments.Length == 1)
                                    {
                                        innerType = genericArguments[0];
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            innerType = null;
            return false;
        }

        public static bool IsLocationGenericType(Type type, out Type? genericArgumentType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == locationGenericType)
            {
                genericArgumentType = type.GetGenericArguments()[0];
                return true;
            }

            genericArgumentType = null;
            return false;
        }

        public static bool IsRuntimeArgumentType(Type propertyType) => TypeHelper.AreTypesCompatible(propertyType, runtimeArgumentType);

        public static bool IsVariableType(Type propertyType, out Type? innerType)
        {
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == variableGenericType)
            {
                innerType = propertyType.GetGenericArguments()[0];
                return true;
            }

            innerType = null;
            return TypeHelper.AreTypesCompatible(propertyType, variableType);
        }

        public static bool IsVariableType(Type propertyType) =>
            propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == variableGenericType
                ? true
                : TypeHelper.AreTypesCompatible(propertyType, variableType);

        public static void ProcessActivityInstanceTree(
            ActivityInstance rootInstance,
            ActivityExecutor? executor,
            Func<ActivityInstance, ActivityExecutor, bool> callback)
        {
            Queue<IList<ActivityInstance>>? instancesRemaining = null;

            var currentInstancesList = new TreeProcessingList();
            currentInstancesList.Add(rootInstance);

            TreeProcessingList? nextInstanceList = null;
            if (rootInstance.HasChildren)
            {
                nextInstanceList = new TreeProcessingList();
            }

            while ((instancesRemaining != null && instancesRemaining.Count > 0)
                || currentInstancesList.Count != 0)
            {
                if (currentInstancesList.Count == 0)
                {
                    Fx.Assert(instancesRemaining != null && instancesRemaining.Count > 0, "This must be the clause that caused us to enter");
                    currentInstancesList.Set(instancesRemaining?.Dequeue());
                }

                for (var i = 0; i < currentInstancesList.Count; i++)
                {
                    var instance = currentInstancesList[i];

                    if (callback(instance, executor) && instance.HasChildren)
                    {
                        Fx.Assert(nextInstanceList != null, "We should have created this list if we are going to get here.");
                        instance.AppendChildren(nextInstanceList, ref instancesRemaining);
                    }
                }

                if (nextInstanceList != null && nextInstanceList.Count > 0)
                {
                    nextInstanceList.TransferTo(currentInstancesList);
                }
                else
                {
                    // We'll just reuse this object on the next pass (Set will be called)
                    currentInstancesList.Reset();
                }
            }
        }

        /// <summary>
        /// Removes the nulls.
        /// </summary>
        /// <param name="list">The list.</param>
        public static void RemoveNulls(IList? list)
        {
            if (list != null)
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] == null)
                    {
                        list.RemoveAt(i);
                    }
                }
            }
        }

        public static bool TryGetArgumentDirectionAndType(Type propertyType, out ArgumentDirection direction, out Type argumentType)
        {
            direction = ArgumentDirection.In; // default to In
            argumentType = TypeHelper.ObjectType;  // default to object

            if (propertyType.IsGenericType)
            {
                argumentType = propertyType.GetGenericArguments()[0];

                var genericType = propertyType.GetGenericTypeDefinition();

                if (genericType == inArgumentGenericType)
                {
                    return true;
                }

                if (genericType == outArgumentGenericType)
                {
                    direction = ArgumentDirection.Out;
                    return true;
                }

                if (genericType == inOutArgumentGenericType)
                {
                    direction = ArgumentDirection.InOut;
                    return true;
                }
            }
            else
            {
                if (propertyType == inArgumentType)
                {
                    return true;
                }

                if (propertyType == outArgumentType)
                {
                    direction = ArgumentDirection.Out;
                    return true;
                }

                if (propertyType == inOutArgumentType)
                {
                    direction = ArgumentDirection.InOut;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetDelegateArgumentDirectionAndType(Type propertyType, out ArgumentDirection direction, out Type argumentType)
        {
            direction = ArgumentDirection.In; // default to In
            argumentType = TypeHelper.ObjectType;  // default to object

            if (propertyType.IsGenericType)
            {
                argumentType = propertyType.GetGenericArguments()[0];

                var genericType = propertyType.GetGenericTypeDefinition();

                if (genericType == delegateInArgumentGenericType)
                {
                    return true;
                }

                if (genericType == delegateOutArgumentGenericType)
                {
                    direction = ArgumentDirection.Out;
                    return true;
                }
            }
            else
            {
                if (propertyType == delegateInArgumentType)
                {
                    return true;
                }

                if (propertyType == delegateOutArgumentType)
                {
                    direction = ArgumentDirection.Out;
                    return true;
                }
            }

            return false;
        }

        internal static void ValidateOrigin(object? origin, Activity activity)
        {
            if (origin != null && (origin is Activity || origin is Argument || origin is ActivityDelegate || origin is LocationReference))
            {
                activity.AddTempValidationError(new ValidationError(SR.OriginCannotBeRuntimeIntrinsic(origin)));
            }
        }

        private static string GetDisplayName(Type sourceType)
        {
            if (sourceType.IsGenericType)
            {
                // start with the type name
                var displayName = sourceType.Name;
                var tickIndex = displayName.IndexOf('`', StringComparison.OrdinalIgnoreCase);

                // remove the tick+number of parameters "generics format". Note that the tick won't
                // exist for nested implicitly generic classes, such as Foo`1+Bar
                if (tickIndex > 0)
                {
                    displayName = displayName.Substring(0, tickIndex);
                }

                // and provide a more readable version based on the closure type names
                var genericArguments = sourceType.GetGenericArguments();
                var stringBuilder = new StringBuilder(displayName);
                stringBuilder.Append("<");
                for (var i = 0; i < genericArguments.Length - 1; i++)
                {
                    stringBuilder.AppendFormat(CultureInfo.CurrentCulture, "{0},", GetDisplayName(genericArguments[i]));
                }

                stringBuilder.AppendFormat(CultureInfo.CurrentCulture, "{0}>", GetDisplayName(genericArguments[genericArguments.Length - 1]));
                return stringBuilder.ToString();
            }
            else
            {
                Fx.Assert(!sourceType.IsGenericTypeDefinition, "we have an actual object, so we should never have a generic type definition");
                return sourceType.Name;
            }
        }

        private static void NoOpCallback(ChildActivity element, ActivityCallStack parentChain)
        {
        }

        private static void ProcessActivity(ChildActivity childActivity, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining, ActivityCallStack parentChain, ref IList<ValidationError> validationErrors, ProcessActivityTreeOptions options, ProcessActivityCallback callback)
        {
            Fx.Assert(options != null, "options should not be null.");

            if (options.CancellationToken.IsCancellationRequested)
            {
                throw FxTrace.Exception.AsError(new OperationCanceledException(options.CancellationToken));
            }

            var activity = childActivity.Activity;
            var constraints = activity.RuntimeConstraints;
            IList<ValidationError> tempValidationErrors = null;

            Fx.Assert(validationErrors == null || !options.StoreTempViolations, "Incoming violations should be null if we are storing them in Activity.tempViolations.");

            if (!activity.HasStartedCachingMetadata)
            {
                // We need to add this activity to the IdSpace first so that we have a meaningful ID
                // for any errors that may occur.
                Fx.Assert(activity.MemberOf != null, "We always set this ahead of time - the root is set in InitializeAsRoot and all others are set in InitializeRelationship.");
                activity.MemberOf.AddMember(activity);

                if (TD.InternalCacheMetadataStartIsEnabled())
                {
                    TD.InternalCacheMetadataStart(activity.Id);
                }
                activity.InternalCacheMetadata(options.CreateEmptyBindings, ref tempValidationErrors);
                if (TD.InternalCacheMetadataStopIsEnabled())
                {
                    TD.InternalCacheMetadataStop(activity.Id);
                }

                ActivityValidationServices.ValidateArguments(activity, activity.Parent == null, ref tempValidationErrors);

                ActivityLocationReferenceEnvironment newPublicEnvironment = null;
                var newImplementationEnvironment = new ActivityLocationReferenceEnvironment(activity.HostEnvironment)
                {
                    InternalRoot = activity
                };

                var nextEnvironmentId = 0;

                ProcessChildren(activity, activity.Children, ActivityCollectionType.Public, true, ref nextActivity, ref activitiesRemaining, ref tempValidationErrors);
                ProcessChildren(activity, activity.ImportedChildren, ActivityCollectionType.Imports, true, ref nextActivity, ref activitiesRemaining, ref tempValidationErrors);
                ProcessChildren(activity, activity.ImplementationChildren, ActivityCollectionType.Implementation, !options.SkipPrivateChildren, ref nextActivity, ref activitiesRemaining, ref tempValidationErrors);

                ProcessArguments(activity, activity.RuntimeArguments, true, ref newImplementationEnvironment, ref nextEnvironmentId, ref nextActivity, ref activitiesRemaining, ref tempValidationErrors);

                ProcessVariables(activity, activity.RuntimeVariables, ActivityCollectionType.Public, true, ref newPublicEnvironment, ref nextEnvironmentId, ref nextActivity, ref activitiesRemaining, ref tempValidationErrors);
                ProcessVariables(activity, activity.ImplementationVariables, ActivityCollectionType.Implementation, !options.SkipPrivateChildren, ref newImplementationEnvironment, ref nextEnvironmentId, ref nextActivity, ref activitiesRemaining, ref tempValidationErrors);

                if (activity.HandlerOf != null)
                {
                    // Since we are a delegate handler we have to do some processing of the handlers
                    // parameters. This is the one part of the tree walk that actually reaches _up_
                    // to process something we've already passed.

                    for (var i = 0; i < activity.HandlerOf.RuntimeDelegateArguments.Count; i++)
                    {
                        var delegateArgument = activity.HandlerOf.RuntimeDelegateArguments[i];
                        var boundArgument = delegateArgument.BoundArgument;
                        if (boundArgument != null)
                        {
                            // At runtime, delegate arguments end up owned by the Handler and are
                            // scoped like public variables of the handler.
                            //
                            // And since they don't own an expression, there's no equivalent
                            // SetupForProcessing method for DelegateArguments
                            if (boundArgument.InitializeRelationship(activity, ref tempValidationErrors))
                            {
                                boundArgument.Id = nextEnvironmentId;
                                nextEnvironmentId++;
                            }
                        }
                    }
                }

                // NOTE: At this point the declared environment is complete (either we're using the
                // parent or we've got a new one)
                if (newPublicEnvironment == null)
                {
                    activity.PublicEnvironment = new ActivityLocationReferenceEnvironment(activity.GetParentEnvironment());
                }
                else
                {
                    if (newPublicEnvironment.Parent == null)
                    {
                        newPublicEnvironment.InternalRoot = activity;
                    }

                    activity.PublicEnvironment = newPublicEnvironment;
                }

                activity.ImplementationEnvironment = newImplementationEnvironment;

                // ProcessDelegates uses activity.Environment
                ProcessDelegates(activity, activity.Delegates, ActivityCollectionType.Public, true, ref nextActivity, ref activitiesRemaining, ref tempValidationErrors);
                ProcessDelegates(activity, activity.ImportedDelegates, ActivityCollectionType.Imports, true, ref nextActivity, ref activitiesRemaining, ref tempValidationErrors);
                ProcessDelegates(activity, activity.ImplementationDelegates, ActivityCollectionType.Implementation, !options.SkipPrivateChildren, ref nextActivity, ref activitiesRemaining, ref tempValidationErrors);

                callback?.Invoke(childActivity, parentChain);

                // copy validation errors in ValidationErrors list
                if (tempValidationErrors != null)
                {
                    if (validationErrors == null)
                    {
                        validationErrors = new List<ValidationError>();
                    }
                    var prefix = ActivityValidationServices.GenerateValidationErrorPrefix(childActivity.Activity, parentChain, options, out var source);

                    for (var i = 0; i < tempValidationErrors.Count; i++)
                    {
                        var validationError = tempValidationErrors[i];

                        validationError.Source = source;
                        validationError.Id = source.Id;

                        if (!string.IsNullOrEmpty(prefix))
                        {
                            validationError.Message = prefix + validationError.Message;
                        }

                        validationErrors.Add(validationError);
                    }
                }

                if (options.StoreTempViolations)
                {
                    if (validationErrors != null)
                    {
                        childActivity.Activity.SetTempValidationErrorCollection(validationErrors);
                        validationErrors = null;
                    }
                }
            }
            else
            {
                // We're processing a reference

                // Add all the children for processing even though they've already been seen.
                SetupForProcessing(activity.Children, true, ref nextActivity, ref activitiesRemaining);
                SetupForProcessing(activity.ImportedChildren, false, ref nextActivity, ref activitiesRemaining);

                SetupForProcessing(activity.RuntimeArguments, ref nextActivity, ref activitiesRemaining);

                SetupForProcessing(activity.RuntimeVariables, ref nextActivity, ref activitiesRemaining);

                SetupForProcessing(activity.Delegates, true, ref nextActivity, ref activitiesRemaining);
                SetupForProcessing(activity.ImportedDelegates, false, ref nextActivity, ref activitiesRemaining);

                if (!options.SkipPrivateChildren)
                {
                    SetupForProcessing(activity.ImplementationChildren, true, ref nextActivity, ref activitiesRemaining);
                    SetupForProcessing(activity.ImplementationDelegates, true, ref nextActivity, ref activitiesRemaining);
                    SetupForProcessing(activity.ImplementationVariables, ref nextActivity, ref activitiesRemaining);
                }

                if (callback != null && !options.OnlyCallCallbackForDeclarations)
                {
                    callback(childActivity, parentChain);
                }

                if (childActivity.Activity.HasTempViolations && !options.StoreTempViolations)
                {
                    childActivity.Activity.TransferTempValidationErrors(ref validationErrors);
                }
            }

            // We only run constraints if the activity could possibly execute and we aren't
            // explicitly skipping them.
            if (!options.SkipConstraints && parentChain.WillExecute && childActivity.CanBeExecuted && constraints.Count > 0)
            {
                ActivityValidationServices.RunConstraints(childActivity, parentChain, constraints, options, false, ref validationErrors);
            }
        }

        private static void ProcessActivityTreeCore(ChildActivity currentActivity, ActivityCallStack? parentChain, ProcessActivityTreeOptions options, ProcessActivityCallback callback, ref IList<ValidationError> validationErrors)
        {
            Fx.Assert(options != null, "We need you to explicitly specify options.");
            Fx.Assert(currentActivity.Activity.MemberOf != null, "We must have an activity with MemberOf setup or we need to skipIdGeneration.");

            var nextActivity = ChildActivity.Empty;
            Stack<ChildActivity> activitiesRemaining = null;

            if (parentChain == null)
            {
                parentChain = new ActivityCallStack();
            }

            if (options.OnlyVisitSingleLevel)
            {
                ProcessActivity(currentActivity, ref nextActivity, ref activitiesRemaining, parentChain, ref validationErrors, options, callback);
            }
            else
            {
                while (!currentActivity.Equals(ChildActivity.Empty))
                {
                    if (object.ReferenceEquals(currentActivity.Activity, popActivity))
                    {
                        var completedParent = parentChain.Pop();
                        completedParent.Activity.SetCached(isSkippingPrivateChildren: options.SkipPrivateChildren);
                    }
                    else
                    {
                        SetupForProcessing(popActivity, true, ref nextActivity, ref activitiesRemaining);
                        ProcessActivity(currentActivity, ref nextActivity, ref activitiesRemaining, parentChain, ref validationErrors, options, callback);
                        parentChain.Push(currentActivity);
                    }

                    // nextActivity is the top of the stack stackTop => nextActivity => currentActivity
                    currentActivity = nextActivity;

                    if (activitiesRemaining != null && activitiesRemaining.Count > 0)
                    {
                        nextActivity = activitiesRemaining.Pop();
                    }
                    else
                    {
                        nextActivity = ChildActivity.Empty;
                    }
                }
            }
        }

        /// <summary>
        /// Processes the arguments.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="arguments">The arguments.</param>
        /// <param name="addChildren">if set to <c>true</c> [add children].</param>
        /// <param name="environment">The environment.</param>
        /// <param name="nextEnvironmentId">The next environment identifier.</param>
        /// <param name="nextActivity">The next activity.</param>
        /// <param name="activitiesRemaining">The activities remaining.</param>
        /// <param name="validationErrors">The validation errors.</param>
        /// <remarks>
        /// Note that we do not need an "isPublicCollection" parameter since all arguments are
        /// public Returns true if there are any non-null expressions
        /// </remarks>
        private static void ProcessArguments(Activity parent, IList<RuntimeArgument> arguments, bool addChildren, ref ActivityLocationReferenceEnvironment environment, ref int nextEnvironmentId, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining, ref IList<ValidationError> validationErrors)
        {
            if (arguments.Count > 0)
            {
                if (environment == null)
                {
                    environment = new ActivityLocationReferenceEnvironment(parent.GetParentEnvironment());
                }

                for (var i = 0; i < arguments.Count; i++)
                {
                    var argument = arguments[i];
                    if (argument.InitializeRelationship(parent, ref validationErrors))
                    {
                        argument.Id = nextEnvironmentId;
                        nextEnvironmentId++;

                        // This must be called after InitializeRelationship since it makes use of RuntimeArgument.Owner;
                        environment.Declare(argument, argument.Owner, ref validationErrors);

                        if (addChildren)
                        {
                            SetupForProcessing(argument, ref nextActivity, ref activitiesRemaining);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Processes the children.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="children">The children.</param>
        /// <param name="collectionType">Type of the collection.</param>
        /// <param name="addChildren">if set to <c>true</c> [add children].</param>
        /// <param name="nextActivity">The next activity.</param>
        /// <param name="activitiesRemaining">The activities remaining.</param>
        /// <param name="validationErrors">The validation errors.</param>
        /// <remarks>Returns true if there are any children</remarks>
        private static void ProcessChildren(Activity parent, IList<Activity> children, ActivityCollectionType collectionType, bool addChildren, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining, ref IList<ValidationError> validationErrors)
        {
            for (var i = 0; i < children.Count; i++)
            {
                var childActivity = children[i];
                if (childActivity.InitializeRelationship(parent, collectionType, ref validationErrors))
                {
                    if (addChildren)
                    {
                        SetupForProcessing(childActivity, collectionType != ActivityCollectionType.Imports, ref nextActivity, ref activitiesRemaining);
                    }
                }
            }
        }

        /// <summary>
        /// Processes the delegates.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="delegates">The delegates.</param>
        /// <param name="collectionType">Type of the collection.</param>
        /// <param name="addChildren">if set to <c>true</c> [add children].</param>
        /// <param name="nextActivity">The next activity.</param>
        /// <param name="activitiesRemaining">The activities remaining.</param>
        /// <param name="validationErrors">The validation errors.</param>
        /// <remarks>Returns true if there are any non-null handlers</remarks>
        private static void ProcessDelegates(Activity parent, IList<ActivityDelegate> delegates, ActivityCollectionType collectionType, bool addChildren, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining, ref IList<ValidationError> validationErrors)
        {
            for (var i = 0; i < delegates.Count; i++)
            {
                var activityDelegate = delegates[i];
                if (activityDelegate.InitializeRelationship(parent, collectionType, ref validationErrors))
                {
                    if (addChildren)
                    {
                        SetupForProcessing(activityDelegate, collectionType != ActivityCollectionType.Imports, ref nextActivity, ref activitiesRemaining);
                    }
                }
            }
        }

        /// <summary>
        /// Processes the variables.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="variables">The variables.</param>
        /// <param name="collectionType">Type of the collection.</param>
        /// <param name="addChildren">if set to <c>true</c> [add children].</param>
        /// <param name="environment">The environment.</param>
        /// <param name="nextEnvironmentId">The next environment identifier.</param>
        /// <param name="nextActivity">The next activity.</param>
        /// <param name="activitiesRemaining">The activities remaining.</param>
        /// <param name="validationErrors">The validation errors.</param>
        /// <remarks>Returns true if there are any non-null defaults</remarks>
        private static void ProcessVariables(Activity parent, IList<Variable> variables, ActivityCollectionType collectionType, bool addChildren, ref ActivityLocationReferenceEnvironment environment, ref int nextEnvironmentId, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining, ref IList<ValidationError> validationErrors)
        {
            if (variables.Count > 0)
            {
                if (environment == null)
                {
                    environment = new ActivityLocationReferenceEnvironment(parent.GetParentEnvironment());
                }

                for (var i = 0; i < variables.Count; i++)
                {
                    var variable = variables[i];
                    if (variable.InitializeRelationship(parent, collectionType == ActivityCollectionType.Public, ref validationErrors))
                    {
                        variable.Id = nextEnvironmentId;
                        nextEnvironmentId++;

                        // This must be called after InitializeRelationship since it makes use of Variable.Owner;
                        environment.Declare(variable, variable.Owner, ref validationErrors);

                        if (addChildren)
                        {
                            SetupForProcessing(variable, ref nextActivity, ref activitiesRemaining);
                        }
                    }
                }
            }
        }

        private static void SetupForProcessing(IList<Activity> children, bool canBeExecuted, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining)
        {
            for (var i = 0; i < children.Count; i++)
            {
                SetupForProcessing(children[i], canBeExecuted, ref nextActivity, ref activitiesRemaining);
            }
        }

        private static void SetupForProcessing(IList<ActivityDelegate> delegates, bool canBeExecuted, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining)
        {
            for (var i = 0; i < delegates.Count; i++)
            {
                SetupForProcessing(delegates[i], canBeExecuted, ref nextActivity, ref activitiesRemaining);
            }
        }

        private static void SetupForProcessing(IList<Variable> variables, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining)
        {
            for (var i = 0; i < variables.Count; i++)
            {
                SetupForProcessing(variables[i], ref nextActivity, ref activitiesRemaining);
            }
        }

        private static void SetupForProcessing(IList<RuntimeArgument> arguments, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining)
        {
            for (var i = 0; i < arguments.Count; i++)
            {
                SetupForProcessing(arguments[i], ref nextActivity, ref activitiesRemaining);
            }
        }

        private static void SetupForProcessing(ActivityDelegate activityDelegate, bool canBeExecuted, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining)
        {
            if (activityDelegate.Handler != null)
            {
                SetupForProcessing(activityDelegate.Handler, canBeExecuted, ref nextActivity, ref activitiesRemaining);
            }
        }

        private static void SetupForProcessing(Variable variable, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining)
        {
            if (variable.Default != null)
            {
                SetupForProcessing(variable.Default, true, ref nextActivity, ref activitiesRemaining);
            }
        }

        private static void SetupForProcessing(RuntimeArgument argument, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining)
        {
            if (argument.BoundArgument != null && !argument.BoundArgument.IsEmpty)
            {
                SetupForProcessing(argument.BoundArgument.Expression, true, ref nextActivity, ref activitiesRemaining);
            }
        }

        /// <summary>
        /// Setups for processing.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <param name="canBeExecuted">if set to <c>true</c> [can be executed].</param>
        /// <param name="nextActivity">The next activity.</param>
        /// <param name="activitiesRemaining">The activities remaining.</param>
        /// <remarks>nextActivity is always the top of the stack</remarks>
        private static void SetupForProcessing(Activity activity, bool canBeExecuted, ref ChildActivity nextActivity, ref Stack<ChildActivity> activitiesRemaining)
        {
            if (!nextActivity.Equals(ChildActivity.Empty))
            {
                if (activitiesRemaining == null)
                {
                    activitiesRemaining = new Stack<ChildActivity>();
                }

                activitiesRemaining.Push(nextActivity);
            }

            nextActivity = new ChildActivity(activity, canBeExecuted);
        }

        private static bool ShouldShortcut(Activity activity, ProcessActivityTreeOptions options)
        {
            if (options.SkipIfCached && options.IsRuntimeReadyOptions)
            {
                return activity.IsRuntimeReady;
            }

            return false;
        }
    }
}
