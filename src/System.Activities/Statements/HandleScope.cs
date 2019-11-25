// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using Portable.Xaml.Markup;

    using System.Activities;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Collections.ObjectModel;

    /// <summary>
    /// The HandleScope class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.NativeActivity" />
    /// </summary>
    /// <typeparam name="THandle">The type of the handle.</typeparam>
    /// <seealso cref="System.Activities.NativeActivity" />
    [ContentProperty("Body")]
    public sealed class HandleScope<THandle> : NativeActivity
        where THandle : Handle
    {
        private Variable<THandle>? declaredHandle;

        /// <summary>
        /// Initializes a new instance of the <see cref="HandleScope{THandle}"/> class.
        /// </summary>
        public HandleScope()
        {
        }

        /// <summary>
        /// Gets or sets the handle.
        /// </summary>
        /// <value>The handle.</value>
        public InArgument<THandle>? Handle { get; set; }

        /// <summary>
        /// Gets or sets the body.
        /// </summary>
        /// <value>The body.</value>
        public Activity? Body { get; set; }

        /// <summary>
        /// Caches the metadata.
        /// </summary>
        /// <param name="metadata">The metadata.</param>
        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            var handleArgument = new RuntimeArgument("Handle", typeof(THandle), ArgumentDirection.In);
            metadata.Bind(this.Handle, handleArgument);
            metadata.SetArgumentsCollection(new Collection<RuntimeArgument> { handleArgument });

            if (this.Body != null)
            {
                metadata.SetChildrenCollection(new Collection<Activity> { this.Body });
            }

            Collection<Variable>? implementationVariables = null;

            if ((this.Handle == null) || this.Handle.IsEmpty)
            {
                if (this.declaredHandle == null)
                {
                    this.declaredHandle = new Variable<THandle>();
                }
            }
            else
            {
                this.declaredHandle = null;
            }

            if (this.declaredHandle != null)
            {
                ActivityUtilities.Add(ref implementationVariables, this.declaredHandle);
            }

            metadata.SetImplementationVariablesCollection(implementationVariables);
        }

        /// <summary>
        /// Executes the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        protected override void Execute(NativeActivityContext context)
        {
            // We should go through the motions even if there is no Body for debugging
            // purposes.  When testing handles people will probably use empty scopes
            // expecting everything except the Body execution to occur.

            Handle? scopedHandle;
            if ((this.Handle == null) || this.Handle.IsEmpty)
            {
                Fx.Assert(this.declaredHandle != null, "We should have declared the variable if we didn't have the argument set.");
                if (this.declaredHandle == null)
                {
                    throw new NullReferenceException("We should have declared the variable if we didn't have the argument set.");
                }

                scopedHandle = this.declaredHandle.Get(context);
            }
            else
            {
                scopedHandle = this.Handle.Get(context);
            }

            if (scopedHandle == null)
            {
                throw FxTrace.Exception.ArgumentNull("Handle");
            }

            context.Properties.Add(scopedHandle.ExecutionPropertyName, scopedHandle);

            if (this.Body != null)
            {
                context.ScheduleActivity(this.Body);
            }
        }
    }
}
