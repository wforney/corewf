// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Statements
{
    using Portable.Xaml.Markup;

    using System.Activities;
    using System.Activities.Runtime;
    using System.Activities.Runtime.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;

    /// <summary>
    /// The FlowSwitch class. This class cannot be inherited. Implements the <see
    /// cref="System.Activities.Statements.FlowNode" /> Implements the <see
    /// cref="System.Activities.Statements.IFlowSwitch" />
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <seealso cref="System.Activities.Statements.FlowNode" />
    /// <seealso cref="System.Activities.Statements.IFlowSwitch" />
    [ContentProperty("Cases")]
    public sealed class FlowSwitch<T> : FlowNode, IFlowSwitch
    {
        /// <summary>
        /// The cases
        /// </summary>
        internal IDictionary<T, FlowNode> cases;

        /// <summary>
        /// The default display name
        /// </summary>
        private const string DefaultDisplayName = "Switch";

        /// <summary>
        /// The on switch completed
        /// </summary>
        private CompletionCallback<T>? onSwitchCompleted;

        /// <summary>
        /// Initializes a new instance of the <see cref="FlowSwitch{T}" /> class.
        /// </summary>
        public FlowSwitch()
        {
            this.cases = new NullableKeyDictionary<T, FlowNode>();
            this.DisplayName = FlowSwitch<T>.DefaultDisplayName;
        }

        /// <summary>
        /// Gets the cases.
        /// </summary>
        /// <value>The cases.</value>
        [Fx.Tag.KnownXamlExternal]
        public IDictionary<T, FlowNode> Cases => this.cases;

        /// <summary>
        /// Gets or sets the default.
        /// </summary>
        /// <value>The default.</value>
        [DefaultValue(null)]
        public FlowNode? Default { get; set; }

        /// <summary>
        /// Gets or sets the display name.
        /// </summary>
        /// <value>The display name.</value>
        [DefaultValue(FlowSwitch<T>.DefaultDisplayName)]
        public string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the expression.
        /// </summary>
        /// <value>The expression.</value>
        [DefaultValue(null)]
        public Activity<T>? Expression { get; set; }

        /// <summary>
        /// Gets the child activity.
        /// </summary>
        /// <value>The child activity.</value>
        internal override Activity? ChildActivity => this.Expression;

        /// <summary>
        /// Executes the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="parent">The parent.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        bool IFlowSwitch.Execute(NativeActivityContext context, Flowchart parent)
        {
            context.ScheduleActivity(this.Expression, this.GetSwitchCompletedCallback(parent));
            return false;
        }

        /// <summary>
        /// Gets the next node.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>FlowNode.</returns>
        FlowNode? IFlowSwitch.GetNextNode(object value)
        {
            var newValue = (T)value;
            if (this.Cases.TryGetValue(newValue, out var result))
            {
                if (TD.FlowchartSwitchCaseIsEnabled())
                {
                    TD.FlowchartSwitchCase(this.Owner?.DisplayName, newValue.ToString());
                }
                
                return result;
            }
            else
            {
                if (this.Default != null)
                {
                    if (TD.FlowchartSwitchDefaultIsEnabled())
                    {
                        TD.FlowchartSwitchDefault(this.Owner?.DisplayName);
                    }
                }
                else
                {
                    if (TD.FlowchartSwitchCaseNotFoundIsEnabled())
                    {
                        TD.FlowchartSwitchCaseNotFound(this.Owner?.DisplayName);
                    }
                }

                return this.Default;
            }
        }

        /// <summary>
        /// Gets the connected nodes.
        /// </summary>
        /// <param name="connections">The connections.</param>
        internal override void GetConnectedNodes(IList<FlowNode> connections)
        {
            foreach (var item in this.Cases)
            {
                connections.Add(item.Value);
            }

            if (this.Default != null)
            {
                connections.Add(this.Default);
            }
        }

        /// <summary>
        /// Called when [open].
        /// </summary>
        /// <param name="owner">The owner.</param>
        /// <param name="metadata">The metadata.</param>
        internal override void OnOpen(Flowchart? owner, NativeActivityMetadata metadata)
        {
            if (this.Expression == null)
            {
                metadata.AddValidationError(SR.FlowSwitchRequiresExpression(owner?.DisplayName));
            }
        }

        /// <summary>
        /// Gets the switch completed callback.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>CompletionCallback&lt;T&gt;.</returns>
        private CompletionCallback<T> GetSwitchCompletedCallback(Flowchart parent)
        {
            if (this.onSwitchCompleted == null)
            {
                this.onSwitchCompleted = new CompletionCallback<T>(parent.OnSwitchCompleted<T>);
            }

            return this.onSwitchCompleted;
        }
    }
}
