// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Expressions
{
    using System;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.Collections;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Threading;
    using System.Windows.Markup;

    /// <summary>
    /// The IndexerReference class. This class cannot be inherited.
    /// Implements the <see cref="CodeActivity{Location{TItem}}" />
    /// </summary>
    /// <typeparam name="TOperand">The type of the t operand.</typeparam>
    /// <typeparam name="TItem">The type of the t item.</typeparam>
    /// <seealso cref="CodeActivity{Location{TItem}}" />
    [ContentProperty("Indices")]
    public sealed class IndexerReference<TOperand, TItem> : CodeActivity<Location<TItem>>
    {
        /// <summary>
        /// The indices
        /// </summary>
        private Collection<InArgument> indices;

        /// <summary>
        /// The get method
        /// </summary>
        private MethodInfo getMethod;

        /// <summary>
        /// The set method
        /// </summary>
        private MethodInfo setMethod;

        /// <summary>
        /// The get function
        /// </summary>
        private Func<object, object[], object> getFunc;

        /// <summary>
        /// The set function
        /// </summary>
        private Func<object, object[], object> setFunc;

        /// <summary>
        /// The function cache
        /// </summary>
        private static readonly MruCache<MethodInfo, Func<object, object[], object>> funcCache =
            new MruCache<MethodInfo, Func<object, object[], object>>(MethodCallExpressionHelper.FuncCacheCapacity);

        /// <summary>
        /// The locker
        /// </summary>
        private static readonly ReaderWriterLockSlim locker = new ReaderWriterLockSlim();

        /// <summary>
        /// Gets or sets the operand.
        /// </summary>
        /// <value>The operand.</value>
        [RequiredArgument]
        [DefaultValue(null)]
        public InArgument<TOperand> Operand { get; set; }

        /// <summary>
        /// Gets the indices.
        /// </summary>
        /// <value>The indices.</value>
        [RequiredArgument]
        [DefaultValue(null)]
        [Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations", Justification = "<Pending>")]
        public Collection<InArgument> Indices
        {
            get
            {
                if (this.indices == null)
                {
                    this.indices = new ValidatingCollection<InArgument>
                    {
                        // disallow null values
                        OnAddValidationCallback = item =>
                        {
                            if (item == null)
                            {
                                throw FxTrace.Exception.ArgumentNull(nameof(item));
                            }
                        },
                    };
                }

                return this.indices;
            }
        }

        /// <summary>
        /// Caches the metadata.
        /// </summary>
        /// <param name="metadata">The metadata.</param>
        protected override void CacheMetadata(CodeActivityMetadata metadata)
        {
            var oldGetMethod = this.getMethod;
            var oldSetMethod = this.setMethod;

            if (typeof(TOperand).IsValueType)
            {
                metadata.AddValidationError(SR.TargetTypeIsValueType(this.GetType().Name, this.DisplayName));
            }

            if (this.Indices.Count == 0)
            {
                metadata.AddValidationError(SR.IndicesAreNeeded(this.GetType().Name, this.DisplayName));
            }
            else
            {
                IndexerHelper.CacheMethod<TOperand, TItem>(this.Indices, ref this.getMethod, ref this.setMethod);
                if (this.setMethod == null)
                {
                    metadata.AddValidationError(SR.SpecialMethodNotFound("set_Item", typeof(TOperand).Name));
                }
            }

            var operandArgument = new RuntimeArgument("Operand", typeof(TOperand), ArgumentDirection.In, true);
            metadata.Bind(this.Operand, operandArgument);
            metadata.AddArgument(operandArgument);

            IndexerHelper.OnGetArguments(this.Indices, this.Result, metadata);
            if (MethodCallExpressionHelper.NeedRetrieve(this.getMethod, oldGetMethod, this.getFunc))
            {
                this.getFunc = MethodCallExpressionHelper.GetFunc(metadata, this.getMethod, funcCache, locker);
            }

            if (MethodCallExpressionHelper.NeedRetrieve(this.setMethod, oldSetMethod, this.setFunc))
            {
                this.setFunc = MethodCallExpressionHelper.GetFunc(metadata, this.setMethod, funcCache, locker);
            }
        }

        /// <summary>
        /// Executes the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>Location&lt;TItem&gt;.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        protected override Location<TItem> Execute(CodeActivityContext context)
        {
            var indicesValue = new object[this.Indices.Count];

            for (var i = 0; i < this.Indices.Count; i++)
            {
                indicesValue[i] = this.Indices[i].Get(context);
            }

            var operandValue = this.Operand.Get(context);
            if (operandValue == null)
            {
                throw FxTrace.Exception.AsError(new InvalidOperationException(SR.MemberCannotBeNull("Operand", this.GetType().Name, this.DisplayName)));
            }

            return new IndexerLocation(operandValue, indicesValue, this.getMethod, this.setMethod, this.getFunc, this.setFunc);
        }

        /// <summary>
        /// The IndexerLocation class.
        /// Implements the <see cref="Location{TItem}" />
        /// </summary>
        /// <seealso cref="Location{TItem}" />
        [DataContract]
        internal class IndexerLocation : Location<TItem>
        {
            /// <summary>
            /// The get function
            /// </summary>
            private readonly Func<object, object[], object> getFunc;

            /// <summary>
            /// The set function
            /// </summary>
            private readonly Func<object, object[], object> setFunc;

            /// <summary>
            /// Initializes a new instance of the <see cref="IndexerLocation"/> class.
            /// </summary>
            /// <param name="operand">The operand.</param>
            /// <param name="indices">The indices.</param>
            /// <param name="getMethod">The get method.</param>
            /// <param name="setMethod">The set method.</param>
            /// <param name="getFunc">The get function.</param>
            /// <param name="setFunc">The set function.</param>
            public IndexerLocation(TOperand operand, object[] indices, MethodInfo getMethod, MethodInfo setMethod,
                Func<object, object[], object> getFunc, Func<object, object[], object> setFunc)
                : base()
            {
                this.SerializedOperand = operand;
                this.SerializedIndices = indices;
                this.SerializedGetMethod = getMethod;
                this.SerializedSetMethod = setMethod;
                this.getFunc = getFunc;
                this.setFunc = setFunc;
            }

            /// <summary>
            /// Gets or sets the value.
            /// </summary>
            /// <value>The value.</value>
            /// <exception cref="InvalidOperationException"></exception>
            public override TItem Value
            {
                get
                {
                    Fx.Assert(this.SerializedOperand != null, "operand must not be null");
                    Fx.Assert(this.SerializedIndices != null, "indices must not be null");
                    if (this.getFunc == null)
                    {
                        if (this.SerializedGetMethod != null)
                        {
                            return (TItem)this.SerializedGetMethod.Invoke(this.SerializedOperand, this.SerializedIndices);
                        }
                    }
                    else
                    {
                        return (TItem)this.getFunc(this.SerializedOperand, this.SerializedIndices);
                    }

                    throw FxTrace.Exception.AsError(new InvalidOperationException(SR.SpecialMethodNotFound("get_Item", typeof(TOperand).Name)));
                }
                set
                {
                    Fx.Assert(this.SerializedSetMethod != null, "setMethod must not be null");
                    Fx.Assert(this.SerializedOperand != null, "operand must not be null");
                    Fx.Assert(this.SerializedIndices != null, "indices must not be null");
                    if (this.SerializedParameters == null)
                    {
                        this.SerializedParameters = new object[this.SerializedIndices.Length + 1];
                        for (var i = 0; i < this.SerializedIndices.Length; i++)
                        {
                            this.SerializedParameters[i] = this.SerializedIndices[i];
                        }

                        this.SerializedParameters[^1] = value;
                    }

                    if (this.setFunc == null)
                    {
                        this.SerializedSetMethod.Invoke(this.SerializedOperand, this.SerializedParameters);
                    }
                    else
                    {
                        this.setFunc(this.SerializedOperand, this.SerializedParameters);
                    }
                }
            }

            /// <summary>
            /// Gets or sets the serialized operand.
            /// </summary>
            /// <value>The serialized operand.</value>
            [DataMember(EmitDefaultValue = false, Name = "operand")]
            internal TOperand SerializedOperand { get; set; }

            /// <summary>
            /// Gets or sets the serialized indices.
            /// </summary>
            /// <value>The serialized indices.</value>
            [DataMember(EmitDefaultValue = false, Name = "indices")]
            internal object[] SerializedIndices { get; set; }

            /// <summary>
            /// Gets or sets the serialized parameters.
            /// </summary>
            /// <value>The serialized parameters.</value>
            [DataMember(EmitDefaultValue = false, Name = "parameters")]
            internal object[] SerializedParameters { get; set; }

            /// <summary>
            /// Gets or sets the serialized get method.
            /// </summary>
            /// <value>The serialized get method.</value>
            [DataMember(EmitDefaultValue = false, Name = "getMethod")]
            internal MethodInfo SerializedGetMethod { get; set; }

            /// <summary>
            /// Gets or sets the serialized set method.
            /// </summary>
            /// <value>The serialized set method.</value>
            [DataMember(EmitDefaultValue = false, Name = "setMethod")]
            internal MethodInfo SerializedSetMethod { get; set; }
        }
    }
}