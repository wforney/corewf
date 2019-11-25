// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;
    using System.Activities.Validation;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Activities.Internals;

    /// <summary>
    /// The RuntimeArgument class. This class cannot be inherited.
    /// Implements the <see cref="LocationReference" />
    /// </summary>
    /// <seealso cref="LocationReference" />
    [Fx.Tag.XamlVisible(false)]
    public sealed partial class RuntimeArgument : LocationReference
    {
        private static InternalEvaluationOrderComparer? evaluationOrderComparer;
        private readonly PropertyDescriptor? bindingProperty;
        private readonly object? bindingPropertyOwner;
        private List<string>? overloadGroupNames;
        private int cacheId;
        private readonly string name;
        private uint nameHash;
        private bool isNameHashSet;
        private readonly Type type;

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeArgument"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="argumentType">Type of the argument.</param>
        /// <param name="direction">The direction.</param>
        public RuntimeArgument(string name, Type? argumentType, ArgumentDirection? direction)
            : this(name, argumentType, direction, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeArgument"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="argumentType">Type of the argument.</param>
        /// <param name="direction">The direction.</param>
        /// <param name="overloadGroupNames">The overload group names.</param>
        public RuntimeArgument(string name, Type? argumentType, ArgumentDirection? direction, List<string>? overloadGroupNames)
            : this(name, argumentType, direction, false, overloadGroupNames)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeArgument"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="argumentType">Type of the argument.</param>
        /// <param name="direction">The direction.</param>
        /// <param name="isRequired">if set to <c>true</c> [is required].</param>
        public RuntimeArgument(string name, Type? argumentType, ArgumentDirection? direction, bool isRequired)
            : this(name, argumentType, direction, isRequired, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeArgument"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="argumentType">Type of the argument.</param>
        /// <param name="direction">The direction.</param>
        /// <param name="isRequired">if set to <c>true</c> [is required].</param>
        /// <param name="overloadGroupNames">The overload group names.</param>
        public RuntimeArgument(string name, Type? argumentType, ArgumentDirection? direction, bool isRequired, List<string>? overloadGroupNames)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(name));
            }

            ArgumentDirectionHelper.Validate(direction, "direction");

            this.name = name;
            this.type = argumentType ?? throw FxTrace.Exception.ArgumentNull(nameof(argumentType));
            this.Direction = direction;
            this.IsRequired = isRequired;
            this.overloadGroupNames = overloadGroupNames;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeArgument"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="argumentType">Type of the argument.</param>
        /// <param name="direction">The direction.</param>
        /// <param name="isRequired">if set to <c>true</c> [is required].</param>
        /// <param name="overloadGroups">The overload groups.</param>
        /// <param name="bindingProperty">The binding property.</param>
        /// <param name="propertyOwner">The property owner.</param>
        internal RuntimeArgument(string name, Type argumentType, ArgumentDirection direction, bool isRequired, List<string> overloadGroups, PropertyDescriptor bindingProperty, object propertyOwner)
            : this(name, argumentType, direction, isRequired, overloadGroups)
        {
            this.bindingProperty = bindingProperty;
            this.bindingPropertyOwner = propertyOwner;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeArgument"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="argumentType">Type of the argument.</param>
        /// <param name="direction">The direction.</param>
        /// <param name="isRequired">if set to <c>true</c> [is required].</param>
        /// <param name="overloadGroups">The overload groups.</param>
        /// <param name="argument">The argument.</param>
        internal RuntimeArgument(string name, Type argumentType, ArgumentDirection direction, bool isRequired, List<string> overloadGroups, Argument argument)
            : this(name, argumentType, direction, isRequired, overloadGroups)
        {
            Fx.Assert(argument != null, "This ctor is only for arguments discovered via reflection in an IDictionary and therefore cannot be null.");

            // Bind straightway since we're not dealing with a property and empty binding isn't an issue.
            Argument.Bind(argument, this);
        }

        /// <summary>
        /// Gets the evaluation order comparer.
        /// </summary>
        /// <value>The evaluation order comparer.</value>
        internal static IComparer<RuntimeArgument> EvaluationOrderComparer
        {
            get
            {
                if (RuntimeArgument.evaluationOrderComparer == null)
                {
                    RuntimeArgument.evaluationOrderComparer = new InternalEvaluationOrderComparer();
                }

                return RuntimeArgument.evaluationOrderComparer;
            }
        }

        /// <summary>
        /// Gets the name core.
        /// </summary>
        /// <value>The name core.</value>
        protected override string NameCore => this.name;

        /// <summary>
        /// Gets the type core.
        /// </summary>
        /// <value>The type core.</value>
        protected override Type TypeCore => this.type;

        /// <summary>
        /// Gets the direction.
        /// </summary>
        /// <value>The direction.</value>
        public ArgumentDirection? Direction { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance is required.
        /// </summary>
        /// <value><c>true</c> if this instance is required; otherwise, <c>false</c>.</value>
        public bool IsRequired { get; private set; }

        /// <summary>
        /// Gets the overload group names.
        /// </summary>
        /// <value>The overload group names.</value>
        public ReadOnlyCollection<string> OverloadGroupNames
        {
            get
            {
                if (this.overloadGroupNames == null)
                {
                    this.overloadGroupNames = new List<string>(0);
                }

                return new ReadOnlyCollection<string>(this.overloadGroupNames);
            }
        }

        internal Activity? Owner { get; private set; }

        internal bool IsInTree => this.Owner != null;

        internal bool IsBound => this.BoundArgument != null;

        internal bool IsEvaluationOrderSpecified => this.IsBound && this.BoundArgument?.EvaluationOrder != Argument.UnspecifiedEvaluationOrder;

        /// <summary>
        /// Gets or sets the bound argument.
        /// </summary>
        /// <value>The bound argument.</value>
        /// <remarks>
        /// We allow this to be set an unlimited number of times.  We also allow it to be set back to null.                
        /// </remarks>
        internal Argument? BoundArgument { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is result.
        /// </summary>
        /// <value><c>true</c> if this instance is result; otherwise, <c>false</c>.</value>
        /// <exception cref="InvalidOperationException"></exception>
        /// <remarks>
        /// returns true if this is the "Result" argument of an <see cref="Activity{T}"/>
        /// </remarks>
        internal bool IsResult
        {
            get
            {
                Fx.Assert(this.Owner != null, Properties.Resources.ShouldOnlyBeCalledWhenArgumentIsBound);
                if (this.Owner == null)
                {
                    throw new InvalidOperationException(Properties.Resources.ShouldOnlyBeCalledWhenArgumentIsBound);
                }

                return this.Owner?.IsResultArgument(this) ?? false;
            }
        }

        internal void SetupBinding(Activity owningElement, bool createEmptyBinding)
        {
            if (this.bindingProperty != null)
            {
                var argument = (Argument)this.bindingProperty.GetValue(this.bindingPropertyOwner);

                if (argument == null)
                {
                    Fx.Assert(this.bindingProperty.PropertyType.IsGenericType, "We only support arguments that are generic types in our reflection walk.");

                    argument = (Argument)Activator.CreateInstance(this.bindingProperty.PropertyType);
                    argument.WasDesignTimeNull = true;

                    if (createEmptyBinding && !this.bindingProperty.IsReadOnly)
                    {
                        this.bindingProperty.SetValue(this.bindingPropertyOwner, argument);
                    }
                }

                Argument.Bind(argument, this);
            }
            else if (!this.IsBound)
            {
                var properties = TypeDescriptor.GetProperties(owningElement);

                PropertyDescriptor? targetProperty = null;

                for (var i = 0; i < properties.Count; i++)
                {
                    var property = properties[i];

                    // We only support auto-setting the property
                    // for generic types.  Otherwise we have no
                    // guarantee that the argument returned by the
                    // property still matches the runtime argument's
                    // type.
                    if (property.Name == this.Name && property.PropertyType.IsGenericType)
                    {
                        if (ActivityUtilities.TryGetArgumentDirectionAndType(property.PropertyType, out var direction, out var argumentType))
                        {
                            if (this.Type == argumentType && this.Direction == direction)
                            {
                                targetProperty = property;
                                break;
                            }
                        }
                    }
                }

                Argument? argument = null;

                if (targetProperty != null)
                {
                    argument = (Argument)targetProperty.GetValue(owningElement);
                }

                if (argument == null)
                {
                    if (targetProperty != null)
                    {
                        if (targetProperty.PropertyType.IsGenericType)
                        {
                            argument = (Argument)Activator.CreateInstance(targetProperty.PropertyType);
                        }
                        else
                        {
                            argument = ActivityUtilities.CreateArgument(this.Type, this.Direction);
                        }

                    }
                    else
                    {
                        argument = ActivityUtilities.CreateArgument(this.Type, this.Direction);
                    }

                    argument.WasDesignTimeNull = true;

                    if (targetProperty != null && createEmptyBinding && !targetProperty.IsReadOnly)
                    {
                        targetProperty.SetValue(owningElement, argument);
                    }
                }

                Argument.Bind(argument, this);
            }

            Fx.Assert(this.IsBound, "We should always be bound when exiting this method.");
        }

        internal bool InitializeRelationship(Activity parent, ref IList<ValidationError> validationErrors)
        {
            if (this.cacheId == parent.CacheId)
            {
                // We're part of the same tree walk
                if (this.Owner == parent)
                {
                    ActivityUtilities.Add(ref validationErrors, this.ProcessViolation(parent, SR.ArgumentIsAddedMoreThanOnce(this.Name, this.Owner.DisplayName)));

                    // Get out early since we've already initialized this argument.
                    return false;
                }

                Fx.Assert(this.Owner != null, "We must have already assigned an owner.");

                ActivityUtilities.Add(
                    ref validationErrors,
                    this.ProcessViolation(
                        parent,
                        SR.ArgumentAlreadyInUse(
                            this.Name,
                            this.Owner?.DisplayName,
                            parent.DisplayName)));

                // Get out early since we've already initialized this argument.
                return false;
            }

            if (this.BoundArgument != null && this.BoundArgument.RuntimeArgument != this)
            {
                ActivityUtilities.Add(
                    ref validationErrors,
                    this.ProcessViolation(
                        parent,
                        SR.RuntimeArgumentBindingInvalid(
                            this.Name,
                            this.BoundArgument.RuntimeArgument?.Name)));

                return false;
            }

            this.Owner = parent;
            this.cacheId = parent.CacheId;

            if (this.BoundArgument != null)
            {
                this.BoundArgument.Validate(parent, ref validationErrors);

                if (!(this.BoundArgument?.IsEmpty ?? true))
                {
                    return this.BoundArgument?.Expression?.InitializeRelationship(this, ref validationErrors) ?? false;
                }
            }

            return true;
        }

        /// <summary>
        /// Tries the populate value.
        /// </summary>
        /// <param name="targetEnvironment">The target environment.</param>
        /// <param name="targetActivityInstance">The target activity instance.</param>
        /// <param name="executor">The executor.</param>
        /// <param name="argumentValueOverride">The argument value override.</param>
        /// <param name="resultLocation">The result location.</param>
        /// <param name="skipFastPath">if set to <c>true</c> [skip fast path].</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        internal bool TryPopulateValue(LocationEnvironment targetEnvironment, ActivityInstance targetActivityInstance, ActivityExecutor executor, object argumentValueOverride, Location resultLocation, bool skipFastPath)
        {
            // We populate values in the following order:
            //   Override
            //   Binding
            //   Default

            Fx.Assert(this.IsBound, "We should ALWAYS be bound at runtime.");
            if (argumentValueOverride != null)
            {
                Fx.Assert(
                    resultLocation == null,
                    "We should never have both an override and a result location unless some day " +
                    "we decide to allow overrides for argument expressions.  If that day comes, we " +
                    "need to deal with potential issues around someone providing and override for " +
                    "a result - with the current code it wouldn't end up in the resultLocation.");

                var location = this.BoundArgument?.CreateDefaultLocation();
                targetEnvironment.Declare(this, location, targetActivityInstance);
                if (location == null)
                {
                    return false;
                }

                location.Value = argumentValueOverride;
                return true;
            }
            else if (!(this.BoundArgument?.IsEmpty ?? true))
            {
                if (skipFastPath)
                {
                    this.BoundArgument?.Declare(targetEnvironment, targetActivityInstance);
                    return false;
                }
                else
                {
                    return this.BoundArgument?.TryPopulateValue(targetEnvironment, targetActivityInstance, executor) ?? false;
                }
            }
            else if (resultLocation != null && this.IsResult)
            {
                targetEnvironment.Declare(this, resultLocation, targetActivityInstance);
                return true;
            }
            else
            {
                var location = this.BoundArgument?.CreateDefaultLocation();
                targetEnvironment.Declare(this, location, targetActivityInstance);
                return true;
            }
        }

        /// <summary>
        /// Gets the location.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>Location.</returns>
        /// <exception cref="InvalidOperationException">
        /// </exception>
        public override Location GetLocation(ActivityContext context)
        {
            if (context == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(context));
            }

            // No need to call context.ThrowIfDisposed explicitly since all
            // the methods/properties on the context will perform that check.

            this.ThrowIfNotInTree();

            Location location;
            if (!context.AllowChainedEnvironmentAccess)
            {
                if (!object.ReferenceEquals(this.Owner, context.Activity))
                {
                    throw FxTrace.Exception.AsError(
                        new InvalidOperationException(SR.CanOnlyGetOwnedArguments(
                            context.Activity.DisplayName,
                            this.Name,
                            this.Owner?.DisplayName)));

                }

                if (object.ReferenceEquals(context.Environment.Definition, context.Activity))
                {
                    if (!context.Environment.TryGetLocation(this.Id, out location))
                    {
                        throw FxTrace.Exception.AsError(new InvalidOperationException(SR.ArgumentDoesNotExistInEnvironment(this.Name)));
                    }
                }
                else
                {
                    Fx.Assert(this.Owner.IsFastPath, "If an activity defines an argument, then it should define an environment, unless it's SkipArgumentResolution");
                    Fx.Assert(this.IsResult, "The only user-accessible argument that a SkipArgumentResolution activity can have is its result");
                    // We need to give the activity access to its result argument because, if it has
                    // no other arguments, it might have been implicitly opted into SkipArgumentResolution
                    location = context.GetIgnorableResultLocation(this);
                }
            }
            else
            {
                Fx.Assert(object.ReferenceEquals(this.Owner, context.Activity) || object.ReferenceEquals(this.Owner, context.Activity.MemberOf.Owner),
                    "This should have been validated by the activity which set AllowChainedEnvironmentAccess.");

                if (!context.Environment.TryGetLocation(this.Id, this.Owner, out location))
                {
                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.ArgumentDoesNotExistInEnvironment(this.Name)));
                }
            }

            return location;
        }

        /// <summary>
        /// Gets the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>System.Nullable&lt;System.Object&gt;.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by
        /// ExpressionUtilities.TryRewriteLambdaExpression.  Update that
        /// file if the signature changes.        
        /// </remarks>
        public object? Get(ActivityContext context) => context?.GetValue<object>(this);

        /// <summary>
        /// Gets the specified context.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="context">The context.</param>
        /// <returns>T.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by
        /// ExpressionUtilities.TryRewriteLambdaExpression.  Update that
        /// file if the signature changes.        
        /// </remarks>
        public T Get<T>(ActivityContext? context) => context == null ? default : context.GetValue<T>(this);

        /// <summary>
        /// Sets the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="value">The value.</param>
        public void Set(ActivityContext? context, object value) => context?.SetValue(this, value);

        /// <summary>
        /// This method exists for the Debugger        
        /// </summary>
        /// <param name="environment">The environment.</param>
        /// <returns>Location.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        internal Location InternalGetLocation(LocationEnvironment environment)
        {
            Fx.Assert(this.IsInTree, "Argument must be opened");

            if (!environment.TryGetLocation(this.Id, this.Owner, out var location))
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.ArgumentDoesNotExistInEnvironment(this.Name)));
            }

            return location;
        }

        /// <summary>
        /// Processes the violation.
        /// </summary>
        /// <param name="owner">The owner.</param>
        /// <param name="errorMessage">The error message.</param>
        /// <returns>ValidationError.</returns>
        private ValidationError ProcessViolation(Activity owner, string errorMessage) =>
            new ValidationError(errorMessage, false, this.Name)
            {
                Source = owner,
                Id = owner.Id
            };

        /// <summary>
        /// Throws if not in tree.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal void ThrowIfNotInTree()
        {
            if (!this.IsInTree)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.RuntimeArgumentNotOpen(this.Name)));
            }
        }

        /// <summary>Ensures the hash.</summary>
        private void EnsureHash()
        {
            if (!this.isNameHashSet)
            {
                this.nameHash = CRCHashCode.Calculate(this.Name);
                this.isNameHashSet = true;
            }
        }
    }
}
