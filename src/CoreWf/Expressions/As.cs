// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{
    using System;
    using System.Activities;
    using System.Activities.Runtime;
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq.Expressions;

    /// <summary>
    /// The As class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.CodeActivity{TResult}" />
    /// </summary>
    /// <typeparam name="TOperand">The type of the t operand.</typeparam>
    /// <typeparam name="TResult">The type of the t result.</typeparam>
    /// <seealso cref="System.Activities.CodeActivity{TResult}" />
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Optimizing for XAML naming. VB imperative users will [] qualify (e.g. New [As])")]
    public sealed class As<TOperand, TResult> : CodeActivity<TResult>
    {
        /// <summary>
        /// The operation function
        /// </summary>
        /// <remarks>
        /// Lock is not needed for operationFunction here. The reason is that delegates for a given As<TLeft, TRight, TResult> are the same.
        /// It's possible that 2 threads are assigning the operationFucntion at the same time. But it's okay because the compiled codes are the same.        
        /// </remarks>
        private static Func<TOperand, TResult> operationFunction;

        /// <summary>
        /// Gets or sets the operand.
        /// </summary>
        /// <value>The operand.</value>
        [RequiredArgument]
        [DefaultValue(null)]
        public InArgument<TOperand> Operand { get; set; }

        /// <summary>
        /// Caches the metadata.
        /// </summary>
        /// <param name="metadata">The metadata.</param>
        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            UnaryExpressionHelper.OnGetArguments(metadata, this.Operand);

            if (operationFunction == null)
            {
                if (!UnaryExpressionHelper.TryGenerateLinqDelegate(ExpressionType.TypeAs, out operationFunction, out var validationError))
                {
                    metadata.AddValidationError(validationError);
                }
            }
        }

        /// <summary>
        /// Executes the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>TResult.</returns>
        protected override TResult Execute(CodeActivityContext context)
        {
            Fx.Assert(operationFunction != null, "OperationFunction must exist.");
            var operandValue = this.Operand.Get(context);
            return operationFunction(operandValue);
        }
    }
}
