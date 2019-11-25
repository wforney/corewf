// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{
    using Portable.Xaml.Markup;

    using System;
    using System.Activities;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.Collections;
    using System.Collections.ObjectModel;
    using System.Reflection;
    using System.Threading;

    //[SuppressMessage(FxCop.Category.Naming, FxCop.Rule.IdentifiersShouldNotMatchKeywords,
    //    Justification = "Optimizing for XAML naming. VB imperative users will [] qualify (e.g. New [New])")]
    //[SuppressMessage(FxCop.Category.Naming, FxCop.Rule.IdentifiersShouldNotHaveIncorrectSuffix,
    //    Justification = "Optimizing for XAML naming.")]    
    /// <summary>
    /// The New class. This class cannot be inherited.
    /// Implements the <see cref="CodeActivity{TResult}" />
    /// </summary>
    /// <typeparam name="TResult">The type of the t result.</typeparam>
    /// <seealso cref="CodeActivity{TResult}" />
    [ContentProperty("Arguments")]
    [Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "<Pending>")]
    public sealed class New<TResult> : CodeActivity<TResult>
    {
        private Collection<Argument>? arguments;
        private Func<object[], TResult>? function;
        private ConstructorInfo? constructorInfo;
        private static readonly MruCache<ConstructorInfo, Func<object[], TResult>> funcCache = 
            new MruCache<ConstructorInfo, Func<object[], TResult>>(MethodCallExpressionHelper.FuncCacheCapacity);
        private static readonly ReaderWriterLockSlim locker = new ReaderWriterLockSlim();


        //[SuppressMessage(FxCop.Category.Naming, FxCop.Rule.PropertyNamesShouldNotMatchGetMethods,
        //    Justification = "Optimizing for XAML naming.")]        
        /// <summary>
        /// Gets the arguments.
        /// </summary>
        /// <value>The arguments.</value>
        [Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations", Justification = "<Pending>")]
        public Collection<Argument> Arguments
        {
            get
            {
                if (this.arguments == null)
                {
                    this.arguments = new ValidatingCollection<Argument>
                    {
                        // disallow null values
                        OnAddValidationCallback = item =>
                        {
                            if (item == null)
                            {
                                throw FxTrace.Exception.ArgumentNull(nameof(item));
                            }
                        }
                    };
                }
                return this.arguments;
            }
        }

        /// <summary>
        /// Caches the metadata.
        /// </summary>
        /// <param name="metadata">The metadata.</param>
        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            var foundError = false;
            var oldConstructorInfo = this.constructorInfo;

            // Loop through each argument, validate it, and if validation
            // passed expose it to the metadata
            var types = new Type?[this.Arguments.Count];
            for (var i = 0; i < this.Arguments.Count; i++)
            {
                var argument = this.Arguments[i];
                if (argument == null || argument.Expression == null)
                {
                    metadata.AddValidationError(SR.ArgumentRequired("Arguments", typeof(New<TResult>)));
                    foundError = true;
                }
                else
                {
                    var runtimeArgument = new RuntimeArgument("Argument" + i, this.arguments?[i].ArgumentType, this.arguments?[i].Direction, true);
                    metadata.Bind(this.arguments?[i], runtimeArgument);
                    metadata.AddArgument(runtimeArgument);
                    types[i] = this.Arguments[i].Direction == ArgumentDirection.In ? this.Arguments[i].ArgumentType : this.Arguments[i].ArgumentType?.MakeByRefType();
                }
            }

            // If we didn't find any errors in the arguments then
            // we can look for an appropriate constructor.
            if (!foundError)
            {
                this.constructorInfo = typeof(TResult).GetConstructor(types);
                if (this.constructorInfo == null && (!typeof(TResult).IsValueType || types.Length > 0))
                {
                    metadata.AddValidationError(SR.ConstructorInfoNotFound(typeof(TResult).Name));
                }
                else if ((this.constructorInfo != oldConstructorInfo) || (this.function == null))
                {
                    this.function = MethodCallExpressionHelper.GetFunc(metadata, this.constructorInfo, funcCache, locker);
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
            var objects = new object[this.Arguments.Count];
            for (var i = 0; i < this.Arguments.Count; i++)
            {
                objects[i] = this.Arguments[i].Get(context);
            }

            var result = this.function == null ? default : this.function(objects);
            
            for (var i = 0; i < this.Arguments.Count; i++)
            {
                var argument = this.Arguments[i];
                if (argument.Direction == ArgumentDirection.InOut || argument.Direction == ArgumentDirection.Out)
                {
                    argument.Set(context, objects[i]);
                }
            }

            return result;
        }

    }
}
