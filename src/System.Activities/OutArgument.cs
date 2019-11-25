// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using Portable.Xaml.Markup;

    using System;
    using System.Activities.Expressions;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.XamlIntegration;
    using System.ComponentModel;
    using System.Linq.Expressions;

    /// <summary>
    /// The OutArgument class.
    /// Implements the <see cref="System.Activities.Argument" />
    /// </summary>
    /// <seealso cref="System.Activities.Argument" />
    public abstract class OutArgument : Argument
    {
        internal OutArgument() => this.Direction = ArgumentDirection.Out;

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters,
        //    Justification = "Subclass needed to enforce rules about which directions can be referenced.")]        
        /// <summary>
        /// Creates the reference.
        /// </summary>
        /// <param name="argumentToReference">The argument to reference.</param>
        /// <param name="referencedArgumentName">Name of the referenced argument.</param>
        /// <returns>OutArgument.</returns>
        public static OutArgument CreateReference(OutArgument argumentToReference, string referencedArgumentName)
        {
            if (argumentToReference == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(argumentToReference));
            }

            if (string.IsNullOrEmpty(referencedArgumentName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(referencedArgumentName));
            }

            return (OutArgument)ActivityUtilities.CreateReferenceArgument(argumentToReference.ArgumentType, ArgumentDirection.Out, referencedArgumentName);
        }

        //[SuppressMessage(FxCop.Category.Design, FxCop.Rule.ConsiderPassingBaseTypesAsParameters,
        //    Justification = "Subclass needed to enforce rules about which directions can be referenced.")]        
        /// <summary>
        /// Creates the reference.
        /// </summary>
        /// <param name="argumentToReference">The argument to reference.</param>
        /// <param name="referencedArgumentName">Name of the referenced argument.</param>
        /// <returns>OutArgument.</returns>
        public static OutArgument CreateReference(InOutArgument argumentToReference, string referencedArgumentName)
        {
            if (argumentToReference == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(argumentToReference));
            }

            if (string.IsNullOrEmpty(referencedArgumentName))
            {
                throw FxTrace.Exception.ArgumentNullOrEmpty(nameof(referencedArgumentName));
            }

            // Note that we explicitly pass Out since we want an OutArgument created
            return (OutArgument)ActivityUtilities.CreateReferenceArgument(argumentToReference.ArgumentType, ArgumentDirection.Out, referencedArgumentName);
        }
    }

    /// <summary>
    /// The OutArgument class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.OutArgument" />
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <seealso cref="System.Activities.OutArgument" />
    [ContentProperty("Expression")]
    [TypeConverter(typeof(OutArgumentConverter))]    
    [ValueSerializer(typeof(ArgumentValueSerializer))]
    public sealed class OutArgument<T> : OutArgument
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OutArgument{T}"/> class.
        /// </summary>
        /// <param name="variable">The variable.</param>
        public OutArgument(Variable variable)
            : this()
        {
            if (variable != null)
            {
                this.Expression = new VariableReference<T> { Variable = variable };
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OutArgument{T}"/> class.
        /// </summary>
        /// <param name="delegateArgument">The delegate argument.</param>
        public OutArgument(DelegateArgument delegateArgument)
            : this()
        {
            if (delegateArgument != null)
            {
                this.Expression = new DelegateArgumentReference<T> { DelegateArgument = delegateArgument };
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OutArgument{T}"/> class.
        /// </summary>
        /// <param name="expression">The expression.</param>
        public OutArgument(Expression<Func<ActivityContext, T>> expression)
            : this()
        {
            if (expression != null)
            {
                this.Expression = new LambdaReference<T>(expression);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OutArgument{T}"/> class.
        /// </summary>
        /// <param name="expression">The expression.</param>
        public OutArgument(Activity<Location<T>> expression)
            : this() => this.Expression = expression;

        /// <summary>
        /// Initializes a new instance of the <see cref="OutArgument{T}"/> class.
        /// </summary>
        public OutArgument()
            : base() => this.ArgumentType = typeof(T);

        /// <summary>
        /// Gets or sets the expression.
        /// </summary>
        /// <value>The expression.</value>
        [DefaultValue(null)]
        public new Activity<Location<T>>? Expression
        {
            get;
            set;
        }

        internal override ActivityWithResult? ExpressionCore
        {
            get => this.Expression;
            set
            {
                if (value == null)
                {
                    this.Expression = null;
                    return;
                }

                if (value is Activity<Location<T>>)
                {
                    this.Expression = (Activity<Location<T>>)value;
                }
                else
                {
                    // We do not verify compatibility here. We will do that
                    // during CacheMetadata in Argument.Validate.
                    this.Expression = new ActivityWithResultWrapper<Location<T>>(value);
                }
            }
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="Variable"/> to <see cref="OutArgument{T}"/>.
        /// </summary>
        /// <param name="variable">The variable.</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator OutArgument<T>(Variable variable) => FromVariable(variable);

        /// <summary>
        /// Performs an implicit conversion from <see cref="DelegateArgument"/> to <see cref="OutArgument{T}"/>.
        /// </summary>
        /// <param name="delegateArgument">The delegate argument.</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator OutArgument<T>(DelegateArgument delegateArgument) => FromDelegateArgument(delegateArgument);

        /// <summary>
        /// Performs an implicit conversion from <see cref="Activity{Location{T}}"/> to <see cref="OutArgument{T}"/>.
        /// </summary>
        /// <param name="expression">The expression.</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator OutArgument<T>(Activity<Location<T>> expression) => FromExpression(expression);

        /// <summary>
        /// Froms the variable.
        /// </summary>
        /// <param name="variable">The variable.</param>
        /// <returns>OutArgument&lt;T&gt;.</returns>
        public static OutArgument<T> FromVariable(Variable variable)
        {
            if (variable == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(variable));
            }

            return new OutArgument<T>(variable);
        }

        /// <summary>
        /// Froms the delegate argument.
        /// </summary>
        /// <param name="delegateArgument">The delegate argument.</param>
        /// <returns>OutArgument&lt;T&gt;.</returns>
        public static OutArgument<T> FromDelegateArgument(DelegateArgument delegateArgument)
        {
            if (delegateArgument == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(delegateArgument));
            }
            return new OutArgument<T>(delegateArgument);
        }

        /// <summary>
        /// Froms the expression.
        /// </summary>
        /// <param name="expression">The expression.</param>
        /// <returns>OutArgument&lt;T&gt;.</returns>
        public static OutArgument<T> FromExpression(Activity<Location<T>> expression)
        {
            if (expression == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(expression));
            }

            return new OutArgument<T>(expression);
        }

        /// <summary>
        /// Gets the location.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>Location&lt;T&gt;.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by
        /// ExpressionUtilities.TryRewriteLambdaExpression.  Update that
        /// file if the signature changes.        
        /// </remarks>
        public new Location<T> GetLocation(ActivityContext context)
        {
            if (context == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(context));
            }

            this.ThrowIfNotInTree();

            return context.GetLocation<T>(this.RuntimeArgument);
        }

        /// <summary>
        /// Gets the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>T.</returns>
        /// <remarks>
        /// Soft-Link: This method is referenced through reflection by
        /// ExpressionUtilities.TryRewriteLambdaExpression.  Update that
        /// file if the signature changes.        
        /// </remarks>
        public new T Get(ActivityContext context) => this.Get<T>(context);

        /// <summary>
        /// Sets the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="value">The value.</param>
        public void Set(ActivityContext context, T value)
        {
            if (context == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(context));
            }

            this.ThrowIfNotInTree();

            context.SetValue(this, value);
        }

        internal override Location CreateDefaultLocation() => Argument.CreateLocation<T>();

        internal override void Declare(LocationEnvironment targetEnvironment, ActivityInstance activityInstance) => targetEnvironment.DeclareTemporaryLocation<Location<T>>(this.RuntimeArgument, activityInstance, true);

        internal override bool TryPopulateValue(LocationEnvironment targetEnvironment, ActivityInstance targetActivityInstance, ActivityExecutor executor)
        {
            Fx.Assert(this.Expression != null, Properties.Resources.ThisShouldOnlyBeCalledForNonEmptyBindings);
            if (this.Expression == null)
            {
                throw new NullReferenceException(Properties.Resources.ThisShouldOnlyBeCalledForNonEmptyBindings);
            }

            if (this.Expression.UseOldFastPath)
            {
                var argumentValue = executor.ExecuteInResolutionContext<Location<T>>(targetActivityInstance, this.Expression);
                targetEnvironment.Declare(this.RuntimeArgument, argumentValue.CreateReference(true), targetActivityInstance);
                return true;
            }
            else
            {
                targetEnvironment.DeclareTemporaryLocation<Location<T>>(this.RuntimeArgument, targetActivityInstance, true);
                return false;
            }
        }
    }
}
