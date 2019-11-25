// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Expressions
{
    using Portable.Xaml;

    using System;
    using System.Activities.Internals;
    using System.Activities.XamlIntegration;
    using System.Collections.Generic;
    using System.Linq.Expressions;

    public class CompiledExpressionInvoker
    {
        private static readonly AttachableMemberIdentifier compiledExpressionRootForImplementationProperty =
            new AttachableMemberIdentifier(typeof(CompiledExpressionInvoker), "CompiledExpressionRootForImplementation");

        private static readonly AttachableMemberIdentifier compiledExpressionRootProperty =
                    new AttachableMemberIdentifier(typeof(CompiledExpressionInvoker), "CompiledExpressionRoot");

        private readonly Activity expressionActivity;
        private readonly bool isReference;
        private readonly IList<LocationReference> locationReferences;
        private readonly Activity metadataRoot;
        private readonly ITextExpression textExpression;
        private CodeActivityPublicEnvironmentAccessor accessor;
        private ICompiledExpressionRoot compiledRoot;
        private int expressionId;
        private CodeActivityMetadata metadata;

        public CompiledExpressionInvoker(ITextExpression expression, bool isReference, CodeActivityMetadata metadata)
        {
            if (metadata == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(metadata));
            }

            this.expressionId = -1;
            this.textExpression = expression ?? throw FxTrace.Exception.ArgumentNull(nameof(expression));
            this.expressionActivity = expression as Activity;
            this.isReference = isReference;
            this.locationReferences = new List<LocationReference>();
            this.metadata = metadata;
            this.accessor = CodeActivityPublicEnvironmentAccessor.Create(this.metadata);

            if (this.expressionActivity == null)
            {
                throw FxTrace.Exception.Argument(nameof(expression), SR.ITextExpressionParameterMustBeActivity);
            }

            var resultActivity = this.expressionActivity as ActivityWithResult;

            this.metadataRoot = metadata.Environment.Root;

            this.ProcessLocationReferences();
        }

        public bool IsStaticallyCompiled
        {
            get;
            private set;
        }

        // Attached property getter for the compiled expression root for the public surface area of
        // an activity
        public static object GetCompiledExpressionRoot(object target)
        {
            AttachablePropertyServices.TryGetProperty(target, compiledExpressionRootProperty, out var value);
            return value;
        }

        // Attached property getter for the compiled expression root for the implementation surface
        // area of an activity
        public static object GetCompiledExpressionRootForImplementation(object target)
        {
            AttachablePropertyServices.TryGetProperty(target, compiledExpressionRootForImplementationProperty, out var value);
            return value;
        }

        // Attached property setter for the compiled expression root for the public surface area of
        // an activity
        public static void SetCompiledExpressionRoot(object target, ICompiledExpressionRoot compiledExpressionRoot)
        {
            if (compiledExpressionRoot == null)
            {
                AttachablePropertyServices.RemoveProperty(target, compiledExpressionRootProperty);
            }
            else
            {
                AttachablePropertyServices.SetProperty(target, compiledExpressionRootProperty, compiledExpressionRoot);
            }
        }

        // Attached property setter for the compiled expression root for the implementation surface
        // area of an activity
        public static void SetCompiledExpressionRootForImplementation(object target, ICompiledExpressionRoot compiledExpressionRoot)
        {
            if (compiledExpressionRoot == null)
            {
                AttachablePropertyServices.RemoveProperty(target, compiledExpressionRootForImplementationProperty);
            }
            else
            {
                AttachablePropertyServices.SetProperty(target, compiledExpressionRootForImplementationProperty, compiledExpressionRoot);
            }
        }

        public object InvokeExpression(ActivityContext activityContext)
        {
            if (activityContext == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(activityContext));
            }

            if (this.compiledRoot == null || this.expressionId < 0)
            {
                if (!TryGetCompiledExpressionRoot(this.expressionActivity, this.metadataRoot, out this.compiledRoot) ||
                    !CanExecuteExpression(this.compiledRoot, out expressionId))
                {
                    if (!TryGetCurrentCompiledExpressionRoot(activityContext, out this.compiledRoot, out this.expressionId))
                    {
                        throw FxTrace.Exception.AsError(new NotSupportedException(SR.TextExpressionMetadataRequiresCompilation(this.expressionActivity.GetType().Name)));
                    }
                }
            }

            return this.compiledRoot.InvokeExpression(this.expressionId, this.locationReferences, activityContext);
        }

        // Internal helper to find the correct ICER for a given expression.
        internal static bool TryGetCompiledExpressionRoot(Activity expression, Activity target, out ICompiledExpressionRoot compiledExpressionRoot)
        {
            var forImplementation = expression.MemberOf != expression.RootActivity.MemberOf;

            return TryGetCompiledExpressionRoot(target, forImplementation, out compiledExpressionRoot);
        }

        // Helper to find the correct ICER for a given expression. This is separate from the above
        // because within this class we switch forImplementation for the same target Activity to
        // matched the ICER model of using one ICER for all expressions in the implementation and
        // root argument defaults.
        internal static bool TryGetCompiledExpressionRoot(Activity target, bool forImplementation, out ICompiledExpressionRoot compiledExpressionRoot)
        {
            if (!forImplementation)
            {
                compiledExpressionRoot = GetCompiledExpressionRoot(target) as ICompiledExpressionRoot;
                if (compiledExpressionRoot != null)
                {
                    return true;
                }
                // Default expressions for Arguments show up in the public surface area If we didn't
                // find an ICER for the public surface area continue and try to use the
                // implementation ICER
            }

            if (target is ICompiledExpressionRoot)
            {
                compiledExpressionRoot = (ICompiledExpressionRoot)target;
                return true;
            }

            compiledExpressionRoot = GetCompiledExpressionRootForImplementation(target) as ICompiledExpressionRoot;
            if (compiledExpressionRoot != null)
            {
                return true;
            }

            compiledExpressionRoot = null;
            return false;
        }

        internal Expression GetExpressionTree()
        {
            if (this.compiledRoot == null || this.expressionId < 0)
            {
                if (!TryGetCompiledExpressionRootAtDesignTime(this.expressionActivity, this.metadataRoot, out this.compiledRoot, out this.expressionId))
                {
                    return null;
                }
            }

            return this.compiledRoot.GetExpressionTreeForExpression(this.expressionId, this.locationReferences);
        }

        private bool CanExecuteExpression(ICompiledExpressionRoot compiledExpressionRoot, out int expressionId)
        {
            if (compiledExpressionRoot.CanExecuteExpression(this.textExpression.ExpressionText, this.isReference, locationReferences, out expressionId))
            {
                return true;
            }

            return false;
        }

        private void CreateRequiredArguments(IList<string> requiredLocationNames)
        {
            LocationReference reference;
            if (requiredLocationNames != null && requiredLocationNames.Count > 0)
            {
                foreach (var name in requiredLocationNames)
                {
                    reference = FindLocationReference(name);
                    if (reference != null)
                    {
                        if (this.isReference)
                        {
                            this.accessor.CreateLocationArgument(reference, true);
                        }
                        else
                        {
                            this.accessor.CreateArgument(reference, ArgumentDirection.In, true);
                        }
                    }
                }
            }
        }

        private bool FindCompiledExpressionRoot(out int exprId, out ICompiledExpressionRoot compiledExpressionRoot)
        {
            var root = this.metadata.CurrentActivity.Parent;

            while (root != null)
            {
                if (CompiledExpressionInvoker.TryGetCompiledExpressionRoot(metadata.CurrentActivity, root, out var currentCompiledExpressionRoot))
                {
                    if (CanExecuteExpression(currentCompiledExpressionRoot, out exprId))
                    {
                        compiledExpressionRoot = currentCompiledExpressionRoot;
                        return true;
                    }
                }
                root = root.Parent;
            }

            exprId = -1;
            compiledExpressionRoot = null;

            return false;
        }

        private LocationReference FindLocationReference(string name)
        {
            LocationReference returnValue = null;

            var current = this.accessor.ActivityMetadata.Environment;
            while (current != null)
            {
                if (current.TryGetLocationReference(name, out returnValue))
                {
                    return returnValue;
                }
                current = current.Parent;
            }

            return returnValue;
        }

        private void ProcessLocationReferences()
        {
            var environments = new Stack<LocationReferenceEnvironment>();
            // Build list of location by enumerating environments in top down order to match the
            // traversal pattern of TextExpressionCompiler
            var current = this.accessor.ActivityMetadata.Environment;
            while (current != null)
            {
                environments.Push(current);
                current = current.Parent;
            }

            foreach (var environment in environments)
            {
                foreach (var reference in environment.GetLocationReferences())
                {
                    if (this.textExpression.RequiresCompilation)
                    {
                        this.accessor.CreateLocationArgument(reference, false);
                    }

                    this.locationReferences.Add(new InlinedLocationReference(reference, this.metadata.CurrentActivity));
                }
            }

            // Scenarios like VBV/R needs to know if they should run their own compiler during
            // CacheMetadata. If we find a compiled expression root, means we're already compiled.
            // So set the IsStaticallyCompiled flag to true
            var foundCompiledExpressionRoot = this.TryGetCompiledExpressionRootAtDesignTime(this.expressionActivity,
               this.metadataRoot,
               out this.compiledRoot,
               out this.expressionId);

            if (foundCompiledExpressionRoot)
            {
                this.IsStaticallyCompiled = true;

                // For compiled C# expressions we create temp auto generated arguments for all
                // locations whether they are used in the expressions or not. The
                // TryGetReferenceToPublicLocation method call above also generates temp arguments
                // for all locations. However for VB expressions, this leads to inconsistency
                // between build time and run time as during build time VB only generates temp
                // arguments for locations that are referenced in the expressions. To maintain
                // consistency the we call the CreateRequiredArguments method seperately to
                // generates auto arguments only for locations that are referenced.
                if (!this.textExpression.RequiresCompilation)
                {
                    var requiredLocationNames = this.compiledRoot.GetRequiredLocations(this.expressionId);
                    this.CreateRequiredArguments(requiredLocationNames);
                }
            }
        }

        private bool TryGetCompiledExpressionRootAtDesignTime(Activity expression, Activity target, out ICompiledExpressionRoot compiledExpressionRoot, out int exprId)
        {
            exprId = -1;
            compiledExpressionRoot = null;
            if (!CompiledExpressionInvoker.TryGetCompiledExpressionRoot(expression, target, out compiledExpressionRoot) ||
                !CanExecuteExpression(compiledExpressionRoot, out exprId))
            {
                return FindCompiledExpressionRoot(out exprId, out compiledExpressionRoot);
            }

            return true;
        }

        private bool TryGetCurrentCompiledExpressionRoot(ActivityContext activityContext, out ICompiledExpressionRoot compiledExpressionRoot, out int expressionId)
        {
            var current = activityContext.CurrentInstance;

            while (current != null && current.Activity != this.metadataRoot)
            {
                if (CompiledExpressionInvoker.TryGetCompiledExpressionRoot(current.Activity, true, out var currentCompiledExpressionRoot))
                {
                    if (CanExecuteExpression(currentCompiledExpressionRoot, out expressionId))
                    {
                        compiledExpressionRoot = currentCompiledExpressionRoot;
                        return true;
                    }
                }
                current = current.Parent;
            }

            compiledExpressionRoot = null;
            expressionId = -1;

            return false;
        }
    }
}
