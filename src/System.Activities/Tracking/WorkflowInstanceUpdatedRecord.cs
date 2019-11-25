// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace CoreWf.Tracking
{
    using System;
    using System.Activities;
    using System.Activities.DynamicUpdate;
    using System.Activities.Runtime;
    using System.Activities.Tracking;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.Serialization;

    /// <summary>
    /// The WorkflowInstanceUpdatedRecord class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.Tracking.WorkflowInstanceRecord" />
    /// </summary>
    /// <seealso cref="System.Activities.Tracking.WorkflowInstanceRecord" />
    [Fx.Tag.XamlVisible(false)]
    [DataContract]
    public sealed class WorkflowInstanceUpdatedRecord : WorkflowInstanceRecord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowInstanceUpdatedRecord"/> class.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="activityDefinitionId">The activity definition identifier.</param>
        /// <param name="originalDefinitionIdentity">The original definition identity.</param>
        /// <param name="updatedDefinitionIdentity">The updated definition identity.</param>
        public WorkflowInstanceUpdatedRecord(Guid instanceId, string activityDefinitionId, WorkflowIdentity originalDefinitionIdentity, WorkflowIdentity updatedDefinitionIdentity)
            : base(instanceId, activityDefinitionId, WorkflowInstanceStates.Updated, updatedDefinitionIdentity) => this.OriginalDefinitionIdentity = originalDefinitionIdentity;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowInstanceUpdatedRecord"/> class.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="activityDefinitionId">The activity definition identifier.</param>
        /// <param name="originalDefinitionIdentity">The original definition identity.</param>
        /// <param name="updatedDefinitionIdentity">The updated definition identity.</param>
        /// <param name="blockingActivities">The blocking activities.</param>
        public WorkflowInstanceUpdatedRecord(Guid instanceId, string activityDefinitionId, WorkflowIdentity  originalDefinitionIdentity, WorkflowIdentity updatedDefinitionIdentity, IList<ActivityBlockingUpdate> blockingActivities)
            : base(instanceId, activityDefinitionId, WorkflowInstanceStates.UpdateFailed, updatedDefinitionIdentity)
        {
            this.OriginalDefinitionIdentity = originalDefinitionIdentity;
            this.BlockingActivities = new List<ActivityBlockingUpdate>(blockingActivities).AsReadOnly();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowInstanceUpdatedRecord"/> class.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="recordNumber">The record number.</param>
        /// <param name="activityDefinitionId">The activity definition identifier.</param>
        /// <param name="originalDefinitionIdentity">The original definition identity.</param>
        /// <param name="updatedDefinitionIdentity">The updated definition identity.</param>
        public WorkflowInstanceUpdatedRecord(Guid instanceId, long recordNumber, string activityDefinitionId, WorkflowIdentity originalDefinitionIdentity, WorkflowIdentity updatedDefinitionIdentity)
            : base(instanceId, recordNumber, activityDefinitionId, WorkflowInstanceStates.Updated, updatedDefinitionIdentity) => this.OriginalDefinitionIdentity = originalDefinitionIdentity;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowInstanceUpdatedRecord"/> class.
        /// </summary>
        /// <param name="instanceId">The instance identifier.</param>
        /// <param name="recordNumber">The record number.</param>
        /// <param name="activityDefinitionId">The activity definition identifier.</param>
        /// <param name="originalDefinitionIdentity">The original definition identity.</param>
        /// <param name="updatedDefinitionIdentity">The updated definition identity.</param>
        /// <param name="blockingActivities">The blocking activities.</param>
        public WorkflowInstanceUpdatedRecord(Guid instanceId, long recordNumber, string activityDefinitionId, WorkflowIdentity originalDefinitionIdentity, WorkflowIdentity updatedDefinitionIdentity, IList<ActivityBlockingUpdate> blockingActivities)
            : base(instanceId, recordNumber, activityDefinitionId, WorkflowInstanceStates.UpdateFailed, updatedDefinitionIdentity)
        {
            this.OriginalDefinitionIdentity = originalDefinitionIdentity;
            this.BlockingActivities = new List<ActivityBlockingUpdate>(blockingActivities).AsReadOnly();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowInstanceUpdatedRecord"/> class.
        /// </summary>
        /// <param name="record">The record.</param>
        WorkflowInstanceUpdatedRecord(WorkflowInstanceUpdatedRecord record)
            : base(record)
        {
            this.OriginalDefinitionIdentity = record.OriginalDefinitionIdentity;
            this.BlockingActivities = record.BlockingActivities;
        }

        /// <summary>
        /// Gets the original definition identity.
        /// </summary>
        /// <value>The original definition identity.</value>
        public WorkflowIdentity OriginalDefinitionIdentity { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance is successful.
        /// </summary>
        /// <value><c>true</c> if this instance is successful; otherwise, <c>false</c>.</value>
        public bool IsSuccessful => this.BlockingActivities == null;

        /// <summary>
        /// Gets the blocking activities.
        /// </summary>
        /// <value>The blocking activities.</value>
        public IList<ActivityBlockingUpdate>? BlockingActivities { get; private set; }

        /// <summary>
        /// Gets or sets the serialized original definition identity.
        /// </summary>
        /// <value>The serialized original definition identity.</value>
        [DataMember(Name = "OriginalDefinitionIdentity")]
        internal WorkflowIdentity SerializedOriginalDefinitionIdentity
        {
            get => this.OriginalDefinitionIdentity;
            set => this.OriginalDefinitionIdentity = value;
        }

        /// <summary>
        /// Gets or sets the serialized blocking activities.
        /// </summary>
        /// <value>The serialized blocking activities.</value>
        [DataMember(Name = "BlockingActivities")]
        internal IList<ActivityBlockingUpdate>? SerializedBlockingActivities
        {
            get => this.BlockingActivities;
            set => this.BlockingActivities = value;
        }

        /// <summary>
        /// Clones this instance.
        /// </summary>
        /// <returns>TrackingRecord.</returns>
        protected internal override TrackingRecord Clone() => new WorkflowInstanceUpdatedRecord(this);

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString() => string.Format(CultureInfo.CurrentCulture,
                "WorkflowInstanceUpdatedRecord {{ InstanceId = {0}, RecordNumber = {1}, EventTime = {2}, ActivityDefinitionId = {3}, State = {4}, OriginalDefinitionIdentity = {5}, UpdatedDefinitionIdentity = {6}, IsSuccessful = {7} }} ",
                this.InstanceId,
                this.RecordNumber,
                this.EventTime,
                this.ActivityDefinitionId,
                this.State,
                this.OriginalDefinitionIdentity,
                this.WorkflowDefinitionIdentity,
                this.IsSuccessful);
    }
}
