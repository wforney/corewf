// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace Test.Common.TestObjects.Activities
{
    using System;
    using System.Activities;
    using System.Activities.Expressions;
    using System.Linq.Expressions;

    using Argument = System.Activities.Argument;

    /// <summary>
    /// The <see cref="TestArgument"/> class.
    /// </summary>
    public abstract class TestArgument
    {
        /// <summary>
        /// The product argument
        /// </summary>
        protected Argument productArgument;

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; set; }

        /// <summary>
        /// Gets the product argument.
        /// </summary>
        /// <value>The product argument.</value>
        public Argument ProductArgument => this.productArgument;
    }

    /// <summary>
    /// The <see cref="TestArgument"/> class.
    /// Implements the <see cref="TestArgument" />
    /// </summary>
    /// <typeparam name="T">The type of the argument.</typeparam>
    /// <seealso cref="TestArgument" />
    public class TestArgument<T> : TestArgument
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestArgument{T}" /> class.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <param name="name">The name.</param>
        /// <remarks>This constructor is used for a special case in PowerShell activity which uses an InArgument without an expression</remarks>
        public TestArgument(Direction direction, string name) =>
            this.productArgument = this.CreateProductArgument(direction, name, null);

        /// <summary>
        /// Initializes a new instance of the <see cref="TestArgument{T}"/> class.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <param name="name">The name.</param>
        /// <param name="valueLiteral">The value literal.</param>
        public TestArgument(Direction direction, string name, T valueLiteral) => this.productArgument = this.CreateProductArgument(direction, name, new Literal<T>(valueLiteral));

        /// <summary>
        /// Initializes a new instance of the <see cref="TestArgument{T}"/> class.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <param name="name">The name.</param>
        /// <param name="valueExpression">The value expression.</param>
        public TestArgument(Direction direction, string name, Expression<Func<ActivityContext, T>> valueExpression) => this.productArgument = this.CreateProductArgument(direction, name, new LambdaValue<T>(valueExpression));

        /// <summary>
        /// Initializes a new instance of the <see cref="TestArgument{T}"/> class.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <param name="name">The name.</param>
        /// <param name="valueVariable">The value variable.</param>
        public TestArgument(Direction direction, string name, Variable<T> valueVariable) => 
            this.productArgument = direction == Direction.In
                ? this.CreateProductArgument(direction, name, new VariableValue<T>(valueVariable))
                : this.CreateProductArgument(direction, name, new VariableReference<T>(valueVariable));

        /// <summary>
        /// Initializes a new instance of the <see cref="TestArgument{T}"/> class.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <param name="name">The name.</param>
        /// <param name="valueActivity">The value activity.</param>
        public TestArgument(Direction direction, string name, TestActivity valueActivity) =>
            this.productArgument = this.CreateProductArgument(
                direction,
                name, 
                (ActivityWithResult)valueActivity.ProductActivity);

        /// <summary>
        /// Creates the product argument.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <param name="name">The name.</param>
        /// <param name="expression">The expression.</param>
        /// <returns>Argument.</returns>
        /// <exception cref="ArgumentException">Unknown direction value - direction</exception>
        private Argument CreateProductArgument(Direction direction, string name, ActivityWithResult expression)
        {
            Argument argument;

            switch (direction)
            {
                case Direction.In:
                    argument = new InArgument<T>();
                    break;
                case Direction.Out:
                    argument = new OutArgument<T>();
                    break;
                case Direction.InOut:
                    argument = new InOutArgument<T>();
                    break;
                default:
                    throw new ArgumentException("Unknown direction value", nameof(direction));
            }

            this.Name = name;
            argument.Expression = expression;

            return argument;
        }
    }
}
