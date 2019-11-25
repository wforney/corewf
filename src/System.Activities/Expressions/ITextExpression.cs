// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{
    using System.Linq.Expressions;

    public interface ITextExpression
    {
        string ExpressionText
        {
            get;
        }

        string Language
        {
            get;
        }

        bool RequiresCompilation
        {
            get;
        }

        Expression GetExpressionTree();
    }
}
