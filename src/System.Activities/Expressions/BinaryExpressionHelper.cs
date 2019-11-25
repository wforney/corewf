// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{
    using System.Activities.Runtime;
    using System.Activities.Validation;
    using System;
    using System.Collections.ObjectModel;
    using System.Linq.Expressions;

    internal static class BinaryExpressionHelper
    {
        public static void OnGetArguments<TLeft, TRight>(CodeActivityMetadata metadata, InArgument<TLeft> left, InArgument<TRight> right)
        {
            var rightArgument = new RuntimeArgument("Right", typeof(TRight), ArgumentDirection.In, true);
            metadata.Bind(right, rightArgument);

            var leftArgument = new RuntimeArgument("Left", typeof(TLeft), ArgumentDirection.In, true);
            metadata.Bind(left, leftArgument);

            metadata.SetArgumentsCollection(
                new Collection<RuntimeArgument>
                {
                    rightArgument,
                    leftArgument
                });
        }

        public static bool TryGenerateLinqDelegate<TLeft, TRight, TResult>(ExpressionType operatorType, out Func<TLeft, TRight, TResult> function, out ValidationError validationError)
        {
            function = null;
            validationError = null;

            var leftParameter = Expression.Parameter(typeof(TLeft), "left");
            var rightParameter = Expression.Parameter(typeof(TRight), "right");

            try
            {
                var binaryExpression = Expression.MakeBinary(operatorType, leftParameter, rightParameter);

                var expressionToCompile = OperatorPermissionHelper.InjectReflectionPermissionIfNecessary(binaryExpression.Method, binaryExpression);
                var lambdaExpression = Expression.Lambda<Func<TLeft, TRight, TResult>>(expressionToCompile, leftParameter, rightParameter);
                function = lambdaExpression.Compile();

                return true;
            }
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                {
                    throw;
                }

                validationError = new ValidationError(e.Message);
                return false;
            }
        }
    }

}
