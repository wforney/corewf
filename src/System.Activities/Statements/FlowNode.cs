// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Statements
{
    using System.Activities;
    using System.Collections.Generic;

    /// <summary>
    /// The FlowNode class.
    /// </summary>
    public abstract class FlowNode
    {
        /// <summary>
        /// The cache identifier
        /// </summary>
        private int cacheId;

        /// <summary>
        /// Initializes a new instance of the <see cref="FlowNode" /> class.
        /// </summary>
        internal FlowNode() => this.Index = -1;

        /// <summary>
        /// Gets the child activity.
        /// </summary>
        /// <value>The child activity.</value>
        internal abstract Activity? ChildActivity { get; }

        /// <summary>
        /// Gets or sets the index.
        /// </summary>
        /// <value>The index.</value>
        internal int Index { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is open.
        /// </summary>
        /// <value><c>true</c> if this instance is open; otherwise, <c>false</c>.</value>
        internal bool IsOpen => this.Owner != null;

        /// <summary>
        /// Gets the owner.
        /// </summary>
        /// <value>The owner.</value>
        internal Flowchart? Owner { get; private set; }

        /// <summary>
        /// Gets the child activities.
        /// </summary>
        /// <param name="children">The children.</param>
        internal void GetChildActivities(ICollection<Activity> children)
        {
            if (this.ChildActivity != null)
            {
                children.Add(this.ChildActivity);
            }
        }

        /// <summary>
        /// Gets the connected nodes.
        /// </summary>
        /// <param name="connections">The connections.</param>
        internal abstract void GetConnectedNodes(IList<FlowNode> connections);

        /// <summary>
        /// Called when [open].
        /// </summary>
        /// <param name="owner">The owner.</param>
        /// <param name="metadata">The metadata.</param>
        internal abstract void OnOpen(Flowchart? owner, NativeActivityMetadata metadata);

        /// <summary>
        /// Opens the specified owner.
        /// </summary>
        /// <param name="owner">The owner.</param>
        /// <param name="metadata">The metadata.</param>
        /// <returns>
        /// <c>true</c> if this is the first time we've visited this node during this pass,
        /// <c>false</c> otherwise.
        /// </returns>
        internal bool Open(Flowchart? owner, NativeActivityMetadata metadata)
        {
            if (this.cacheId == owner?.CacheId)
            {
                // We've already visited this node during this pass
                if (!object.ReferenceEquals(this.Owner, owner))
                {
                    metadata.AddValidationError(SR.FlowNodeCannotBeShared(this.Owner?.DisplayName, owner.DisplayName));
                }

                // Whether we found an issue or not we don't want to change the metadata during this pass.
                return false;
            }

            // if owner.ValidateUnconnectedNodes - Flowchart will be responsible for calling OnOpen
            // for all the Nodes (connected and unconnected)
            if (!(owner?.ValidateUnconnectedNodes ?? false))
            {
                this.OnOpen(owner, metadata);
            }

            this.Owner = owner;
            this.cacheId = owner?.CacheId ?? -1;
            this.Index = -1;

            return true;
        }
    }
}
