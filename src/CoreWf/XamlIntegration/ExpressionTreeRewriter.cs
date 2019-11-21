// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.XamlIntegration
{
    using System.Activities.Expressions;
    using System.Collections.Generic;
    using System.Linq.Expressions;

    internal class ExpressionTreeRewriter : ExpressionVisitor
    {
        private readonly IList<LocationReference> locationReferences;

        public ExpressionTreeRewriter()
        {
        }

        public ExpressionTreeRewriter(IList<LocationReference> locationReferences)
        {
            this.locationReferences = locationReferences;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            Expression newNode = null;
            if (node.Expression != null && node.Expression.NodeType == ExpressionType.Constant)
            {
                var constExpr = (ConstantExpression)node.Expression;
                if (typeof(CompiledDataContext).IsAssignableFrom(constExpr.Type) && 
                    this.TryRewriteMemberExpressionNode(node, out newNode))
                {
                    return newNode;
                }
            }
            
            return base.VisitMember(node);
        }       

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value != null && node.Value.GetType() == typeof(InlinedLocationReference))
            {
                Expression newNode = node;
                var inlinedReference = (ILocationReferenceWrapper)node.Value;
                if (inlinedReference != null)
                {
                    newNode = Expression.Constant(inlinedReference.LocationReference, typeof(LocationReference));
                    return newNode;
                }
            }

            return base.VisitConstant(node);
        }

        private bool TryRewriteMemberExpressionNode(MemberExpression node, out Expression newNode)
        {
            newNode = null;
            if (this.locationReferences != null)
            {
                foreach (var locationReference in this.locationReferences)
                {
                    if (node.Member.Name == locationReference.Name)
                    {
                        if (locationReference is ILocationReferenceWrapper)
                        {
                            newNode = ExpressionUtilities.CreateIdentifierExpression(((ILocationReferenceWrapper)locationReference).LocationReference);
                        }
                        else
                        {
                            newNode = ExpressionUtilities.CreateIdentifierExpression(locationReference);
                        }
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
