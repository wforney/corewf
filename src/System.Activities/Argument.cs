// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using Portable.Xaml.Markup;

    using System;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Validation;
    using System.Activities.XamlIntegration;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.Serialization;

    /// <summary>
    /// The Argument class.
    /// </summary>
    public abstract partial class Argument
    {
        /// <summary>
        /// The result value
        /// </summary>
        public const string ResultValue = "Result";

        /// <summary>
        /// The unspecified evaluation order
        /// </summary>
        public static readonly int UnspecifiedEvaluationOrder = -1;

        private ArgumentDirection direction;
        private int evaluationOrder;

        /// <summary>
        /// Initializes a new instance of the <see cref="Argument" /> class.
        /// </summary>
        internal Argument() => this.evaluationOrder = UnspecifiedEvaluationOrder;

        /// <summary>
        /// Gets the type of the argument.
        /// </summary>
        /// <value>The type of the argument.</value>
        public Type? ArgumentType { get; internal set; }

        /// <summary>
        /// Gets the direction.
        /// </summary>
        /// <value>The direction.</value>
        public ArgumentDirection Direction
        {
            get => this.direction;
            internal set
            {
                ArgumentDirectionHelper.Validate(value, "value");
                this.direction = value;
            }
        }

        /// <summary>
        /// Gets or sets the evaluation order.
        /// </summary>
        /// <value>The evaluation order.</value>
        [DefaultValue(-1)]
        public int EvaluationOrder
        {
            get => this.evaluationOrder;
            set
            {
                if (value < 0 && value != UnspecifiedEvaluationOrder)
                {
                    throw FxTrace.Exception.ArgumentOutOfRange("EvaluationOrder", value, SR.InvalidEvaluationOrderValue);
                }

                this.evaluationOrder = value;
            }
        }

        /// <summary>
        /// Gets or sets the expression.
        /// </summary>
        /// <value>The expression.</value>
        /// <remarks>this member is repeated by all subclasses, which we control</remarks>
        [IgnoreDataMember]
        [DefaultValue(null)]
        public ActivityWithResult? Expression
        {
            get => this.ExpressionCore;
            set => this.ExpressionCore = value;
        }

        /// <summary>
        /// Gets a value indicating whether this instance is in tree.
        /// </summary>
        /// <value><c>true</c> if this instance is in tree; otherwise, <c>false</c>.</value>
        internal bool IsInTree => (this.RuntimeArgument != null && this.RuntimeArgument.IsInTree);

        /// <summary>
        /// Gets or sets the expression core.
        /// </summary>
        /// <value>The expression core.</value>
        internal abstract ActivityWithResult? ExpressionCore { get; set; }

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        /// <value>The identifier.</value>
        internal int Id
        {
            get
            {
                Fx.Assert(this.RuntimeArgument != null, Properties.Resources.WeShouldNotCallIdUnlessWeHaveARuntimeArgument);
                if (this.RuntimeArgument == null)
                {
                    throw new InvalidOperationException(Properties.Resources.WeShouldNotCallIdUnlessWeHaveARuntimeArgument);
                }

                return this.RuntimeArgument.Id;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is empty.
        /// </summary>
        /// <value><c>true</c> if this instance is empty; otherwise, <c>false</c>.</value>
        internal bool IsEmpty => this.Expression == null;

        /// <summary>
        /// Gets or sets the runtime argument.
        /// </summary>
        /// <value>The runtime argument.</value>
        internal RuntimeArgument? RuntimeArgument { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [was design time null].
        /// </summary>
        /// <value><c>true</c> if [was design time null]; otherwise, <c>false</c>.</value>
        internal bool WasDesignTimeNull { get; set; }

        /// <summary>
        /// Creates the specified type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="direction">The direction.</param>
        /// <returns>Argument.</returns>
        public static Argument Create(Type? type, ArgumentDirection direction) => ActivityUtilities.CreateArgument(type, direction);

        /// <summary>
        /// Creates the reference.
        /// </summary>
        /// <param name="argumentToReference">The argument to reference.</param>
        /// <param name="referencedArgumentName">Name of the referenced argument.</param>
        /// <returns>Argument.</returns>
        public static Argument CreateReference(Argument argumentToReference, string referencedArgumentName)
        {
            if (argumentToReference == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(argumentToReference));
            }

            if (string.IsNullOrEmpty(referencedArgumentName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(referencedArgumentName));
            }

            return ActivityUtilities.CreateReferenceArgument(argumentToReference.ArgumentType, argumentToReference.Direction, referencedArgumentName);
        }

        /// <summary>
        /// Gets the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>System.Object.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by
        /// ExpressionUtilities.TryRewriteLambdaExpression. Update that file if the signature changes.
        /// </remarks>
        public object Get(ActivityContext context) => this.Get<object>(context);

        /// <summary>
        /// Gets the specified context.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="context">The context.</param>
        /// <returns>T.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by
        /// ExpressionUtilities.TryRewriteLambdaExpression. Update that file if the signature changes.
        /// </remarks>
        public T Get<T>(ActivityContext context)
        {
            if (context == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(context));
            }

            this.ThrowIfNotInTree();

            return context.GetValue<T>(this.RuntimeArgument);
        }

        /// <summary>
        /// Gets the location.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>Location.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by
        /// ExpressionUtilities.TryRewriteLambdaExpression. Update that file if the signature changes.
        /// </remarks>
        public Location? GetLocation(ActivityContext context)
        {
            if (context == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(context));
            }

            this.ThrowIfNotInTree();

            return this.RuntimeArgument?.GetLocation(context);
        }

        /// <summary>
        /// Sets the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="value">The value.</param>
        public void Set(ActivityContext context, object value)
        {
            if (context == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(context));
            }

            this.ThrowIfNotInTree();

            context.SetValue(this.RuntimeArgument, value);
        }

        /// <summary>
        /// Binds the specified binding.
        /// </summary>
        /// <param name="binding">The binding.</param>
        /// <param name="argument">The argument.</param>
        internal static void Bind(Argument? binding, RuntimeArgument argument)
        {
            if (binding != null)
            {
                Fx.Assert(binding.Direction == argument.Direction, "The directions must match.");
                Fx.Assert(binding.ArgumentType == argument.Type, "The types must match.");

                binding.RuntimeArgument = argument;
            }

            argument.BoundArgument = binding;
        }

        /// <summary>
        /// Creates the location.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>Location&lt;T&gt;.</returns>
        internal static Location<T> CreateLocation<T>() => new Location<T>();

        /// <summary>
        /// Tries the bind.
        /// </summary>
        /// <param name="binding">The binding.</param>
        /// <param name="argument">The argument.</param>
        /// <param name="violationOwner">The violation owner.</param>
        internal static void TryBind(Argument? binding, RuntimeArgument argument, Activity violationOwner)
        {
            if (argument == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(argument));
            }

            var passedValidations = true;

            if (binding != null)
            {
                if (binding.Direction != argument.Direction)
                {
                    violationOwner.AddTempValidationError(new ValidationError(SR.ArgumentDirectionMismatch(argument.Name, argument.Direction, binding.Direction)));
                    passedValidations = false;
                }

                if (binding.ArgumentType != argument.Type)
                {
                    violationOwner.AddTempValidationError(new ValidationError(SR.ArgumentTypeMismatch(argument.Name, argument.Type, binding.ArgumentType)));
                    passedValidations = false;
                }
            }

            if (passedValidations)
            {
                Bind(binding, argument);
            }
        }

        /// <summary>
        /// Determines whether this instance [can convert to string] the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>
        /// <c>true</c> if this instance [can convert to string] the specified context; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>for ArgumentValueSerializer</remarks>
        internal bool CanConvertToString(IValueSerializerContext context)
        {
            if (this.WasDesignTimeNull)
            {
                return true;
            }
            else
            {
                if (this.EvaluationOrder == UnspecifiedEvaluationOrder)
                {
                    return ActivityWithResultValueSerializer.CanConvertToStringWrapper(this.Expression, context);
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Converts to string.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>System.String.</returns>
        internal string? ConvertToString(IValueSerializerContext context)
        {
            if (this.WasDesignTimeNull)
            {
                // this argument instance was artificially created by the runtime to Xaml, this
                // should appear as {x:Null}
                return null;
            }

            return ActivityWithResultValueSerializer.ConvertToStringWrapper(this.Expression, context);
        }

        /// <summary>
        /// Creates the default location.
        /// </summary>
        /// <returns>Location.</returns>
        internal abstract Location CreateDefaultLocation();

        /// <summary>
        /// Declares the specified target environment.
        /// </summary>
        /// <param name="targetEnvironment">The target environment.</param>
        /// <param name="activityInstance">The activity instance.</param>
        internal abstract void Declare(LocationEnvironment targetEnvironment, ActivityInstance activityInstance);

        /// <summary>
        /// Throws if not in tree.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal void ThrowIfNotInTree()
        {
            if (!this.IsInTree)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.ArgumentNotInTree(this.ArgumentType)));
            }
        }

        /// <summary>
        /// Tries the populate value.
        /// </summary>
        /// <param name="targetEnvironment">The target environment.</param>
        /// <param name="targetActivityInstance">The target activity instance.</param>
        /// <param name="executor">The executor.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        /// <remarks>optional "fast-path" for arguments that can be resolved synchronously</remarks>
        internal abstract bool TryPopulateValue(LocationEnvironment targetEnvironment, ActivityInstance targetActivityInstance, ActivityExecutor executor);

        /// <summary>
        /// Validates the specified owner.
        /// </summary>
        /// <param name="owner">The owner.</param>
        /// <param name="validationErrors">The validation errors.</param>
        internal void Validate(Activity owner, ref IList<ValidationError> validationErrors)
        {
            if (this.Expression != null)
            {
                if (this.Expression.Result != null && !this.Expression.Result.IsEmpty)
                {
                    var validationError = new ValidationError(SR.ResultCannotBeSetOnArgumentExpressions, false, this.RuntimeArgument?.Name, owner);
                    ActivityUtilities.Add(ref validationErrors, validationError);
                }

                var actualExpression = this.Expression;

                if (actualExpression is IExpressionWrapper)
                {
                    actualExpression = ((IExpressionWrapper)actualExpression).InnerExpression;
                }

                switch (this.Direction)
                {
                    case ArgumentDirection.In:
                        if (actualExpression.ResultType != this.ArgumentType)
                        {
                            ActivityUtilities.Add(
                                ref validationErrors,
                                new ValidationError(SR.ArgumentValueExpressionTypeMismatch(this.ArgumentType, actualExpression.ResultType), false, this.RuntimeArgument?.Name, owner));
                        }
                        break;

                    case ArgumentDirection.InOut:
                    case ArgumentDirection.Out:
                        Type locationType;
                        if (!ActivityUtilities.IsLocationGenericType(actualExpression.ResultType, out locationType) ||
                            locationType != this.ArgumentType)
                        {
                            var expectedType = ActivityUtilities.CreateActivityWithResult(ActivityUtilities.CreateLocation(this.ArgumentType));
                            ActivityUtilities.Add(
                                ref validationErrors,
                                new ValidationError(SR.ArgumentLocationExpressionTypeMismatch(expectedType.FullName, actualExpression.GetType().FullName), false, this.RuntimeArgument?.Name, owner));
                        }
                        break;
                }
            }
        }
    }
}
