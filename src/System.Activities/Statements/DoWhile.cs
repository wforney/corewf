// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Statements
{
    using Portable.Xaml.Markup;

    using System;
    using System.Activities;
    using System.Activities.Expressions;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Activities.Runtime.Collections;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq.Expressions;

    /// <summary>
    /// The DoWhile class. This class cannot be inherited. Implements the <see
    /// cref="System.Activities.NativeActivity" />
    /// </summary>
    /// <seealso cref="System.Activities.NativeActivity" />
    [ContentProperty("Body")]
    public sealed class DoWhile : NativeActivity
    {
        /// <summary>
        /// The on body complete
        /// </summary>
        private CompletionCallback? onBodyComplete;

        /// <summary>
        /// The on condition complete
        /// </summary>
        private CompletionCallback<bool>? onConditionComplete;

        /// <summary>
        /// The variables
        /// </summary>
        private Collection<Variable>? variables;

        /// <summary>
        /// Initializes a new instance of the <see cref="DoWhile" /> class.
        /// </summary>
        public DoWhile()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DoWhile" /> class.
        /// </summary>
        /// <param name="condition">The condition.</param>
        public DoWhile(Expression<Func<ActivityContext, bool>> condition)
            : this()
        {
            if (condition == null)
            {
                throw FxTrace.Exception.ArgumentNull(nameof(condition));
            }

            this.Condition = new LambdaValue<bool>(condition);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DoWhile" /> class.
        /// </summary>
        /// <param name="condition">The condition.</param>
        public DoWhile(Activity<bool> condition)
            : this() => this.Condition = condition ?? throw FxTrace.Exception.ArgumentNull(nameof(condition));

        /// <summary>
        /// Gets or sets the body.
        /// </summary>
        /// <value>The body.</value>
        [DefaultValue(null)]
        [DependsOn("Condition")]
        public Activity? Body { get; set; }

        /// <summary>
        /// Gets or sets the condition.
        /// </summary>
        /// <value>The condition.</value>
        [DefaultValue(null)]
        [DependsOn("Variables")]
        public Activity<bool>? Condition { get; set; }

        /// <summary>
        /// Gets the variables.
        /// </summary>
        /// <value>The variables.</value>
        public Collection<Variable> Variables
        {
            get
            {
                if (this.variables == null)
                {
                    this.variables = new ValidatingCollection<Variable>
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
                return this.variables;
            }
        }

        /// <summary>
        /// Caches the metadata.
        /// </summary>
        /// <param name="metadata">The metadata.</param>
        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            metadata.SetVariablesCollection(this.Variables);

            if (this.Condition == null)
            {
                metadata.AddValidationError(SR.DoWhileRequiresCondition(this.DisplayName));
            }
            else
            {
                metadata.AddChild(this.Condition);
            }

            metadata.AddChild(this.Body);
        }

        /// <summary>
        /// Executes the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        protected override void Execute(NativeActivityContext context) =>
            // initial logic is the same as when the condition completes with true
            this.OnConditionComplete(context, null, true);

        /// <summary>
        /// Called when [create dynamic update map].
        /// </summary>
        /// <param name="metadata">The metadata.</param>
        /// <param name="originalActivity">The original activity.</param>
        protected override void OnCreateDynamicUpdateMap(DynamicUpdate.NativeActivityUpdateMapMetadata metadata, Activity originalActivity) =>
            metadata.AllowUpdateInsideThisActivity();

        /// <summary>
        /// Called when [body complete].
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="completedInstance">The completed instance.</param>
        private void OnBodyComplete(NativeActivityContext context, ActivityInstance completedInstance) => 
            this.ScheduleCondition(context);

        /// <summary>
        /// Called when [condition complete].
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="completedInstance">The completed instance.</param>
        /// <param name="result">if set to <c>true</c> [result].</param>
        private void OnConditionComplete(NativeActivityContext context, ActivityInstance? completedInstance, bool result)
        {
            if (result)
            {
                if (this.Body == null)
                {
                    this.ScheduleCondition(context);
                }
                else
                {
                    if (this.onBodyComplete == null)
                    {
                        this.onBodyComplete = new CompletionCallback(this.OnBodyComplete);
                    }

                    context.ScheduleActivity(this.Body, this.onBodyComplete);
                }
            }
        }

        /// <summary>
        /// Schedules the condition.
        /// </summary>
        /// <param name="context">The context.</param>
        private void ScheduleCondition(NativeActivityContext context)
        {
            Fx.Assert(this.Condition != null, "validated in OnOpen");
            if (this.onConditionComplete == null)
            {
                this.onConditionComplete = new CompletionCallback<bool>(this.OnConditionComplete);
            }

            context.ScheduleActivity(this.Condition, this.onConditionComplete);
        }
    }
}
