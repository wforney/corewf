// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Expressions
{

    using System;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.Collections;
    using System.Activities.Statements;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Reflection;
    using System.Threading;
    using System.Windows.Markup;

    [ContentProperty("Parameters")]
    public sealed class InvokeMethod<TResult> : AsyncCodeActivity<TResult>
    {
        private static readonly MruCache<MethodInfo, Func<object, object[], object>> funcCache =
            new MruCache<MethodInfo, Func<object, object[], object>>(MethodCallExpressionHelper.FuncCacheCapacity);

        private static readonly ReaderWriterLockSlim locker = new ReaderWriterLockSlim();
        private Collection<Type> genericTypeArguments;
        private MethodExecutor methodExecutor;
        private MethodResolver methodResolver;
        private Collection<Argument> parameters;
        private RuntimeArgument resultArgument;

        public Collection<Type> GenericTypeArguments
        {
            get
            {
                if (this.genericTypeArguments == null)
                {
                    this.genericTypeArguments = new ValidatingCollection<Type>
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
                return this.genericTypeArguments;
            }
        }

        public string MethodName
        {
            get;
            set;
        }

        public Collection<Argument> Parameters
        {
            get
            {
                if (this.parameters == null)
                {
                    this.parameters = new ValidatingCollection<Argument>
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
                return this.parameters;
            }
        }

        [DefaultValue(false)]
        public bool RunAsynchronously
        {
            get;
            set;
        }

        [DefaultValue(null)]
        public InArgument TargetObject
        {
            get;
            set;
        }

        [DefaultValue(null)]
        public Type TargetType
        {
            get;
            set;
        }

        protected override IAsyncResult BeginExecute(AsyncCodeActivityContext context, AsyncCallback callback, object state)
        {
            return this.methodExecutor.BeginExecuteMethod(context, callback, state);
        }

        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            var arguments = new Collection<RuntimeArgument>();

            var targetObjectType = TypeHelper.ObjectType;

            if (this.TargetObject != null)
            {
                targetObjectType = this.TargetObject.ArgumentType;
            }

            var targetObjectArgument = new RuntimeArgument("TargetObject", targetObjectType, ArgumentDirection.In);
            metadata.Bind(this.TargetObject, targetObjectArgument);
            arguments.Add(targetObjectArgument);

            this.resultArgument = new RuntimeArgument("Result", typeof(TResult), ArgumentDirection.Out);
            metadata.Bind(this.Result, this.resultArgument);
            arguments.Add(this.resultArgument);

            // Parameters are named according to MethodInfo name if DetermineMethodInfo succeeds,
            // otherwise arbitrary names are used.
            this.methodResolver = CreateMethodResolver();

            this.methodResolver.DetermineMethodInfo(metadata, funcCache, locker, ref this.methodExecutor);
            this.methodResolver.RegisterParameters(arguments);

            metadata.SetArgumentsCollection(arguments);

            this.methodResolver.Trace();

            if (this.methodExecutor != null)
            {
                this.methodExecutor.Trace(this);
            }
        }

        protected override TResult EndExecute(AsyncCodeActivityContext context, IAsyncResult result)
        {
            this.methodExecutor.EndExecuteMethod(context, result);
            return this.Result.Get(context);
        }

        private MethodResolver CreateMethodResolver()
        {
            return new MethodResolver
            {
                MethodName = this.MethodName,
                RunAsynchronously = this.RunAsynchronously,
                TargetType = this.TargetType,
                TargetObject = this.TargetObject,
                GenericTypeArguments = this.GenericTypeArguments,
                Parameters = this.Parameters,
                Result = this.resultArgument,
                ResultType = typeof(TResult),
                Parent = this
            };
        }
    }
}
