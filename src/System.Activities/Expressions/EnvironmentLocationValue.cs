// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{
    using System.Activities.Runtime;

    [Fx.Tag.XamlVisible(false)]
    public class EnvironmentLocationValue<T> : CodeActivity<T>, IExpressionContainer, ILocationReferenceExpression
    {
        private readonly LocationReference locationReference;

        // Ctors are internal because we rely on validation from creator or descendant
        internal EnvironmentLocationValue() => this.UseOldFastPath = true;

        internal EnvironmentLocationValue(LocationReference locationReference)
            : this() => this.locationReference = locationReference;

        public virtual LocationReference LocationReference => this.locationReference;

        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            // the creator of this activity is expected to have checked visibility of LocationReference.
            // we override the base CacheMetadata to avoid unnecessary reflection overhead.
        }

        protected override T Execute(CodeActivityContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            try
            {
                context.AllowChainedEnvironmentAccess = true;
                return context.GetValue<T>(this.LocationReference);
            }
            finally
            {
                context.AllowChainedEnvironmentAccess = false;
            }
        }

        ActivityWithResult ILocationReferenceExpression.CreateNewInstance(LocationReference locationReference) =>
            new EnvironmentLocationValue<T>(locationReference);
    }
}
