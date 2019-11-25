// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

using System.Activities.Runtime;
using System;
using System.Globalization;
using System.Runtime.Serialization;

namespace System.Activities.Tracking
{
    [Fx.Tag.XamlVisible(false)]
    [DataContract]
    public sealed class WorkflowInstanceSuspendedRecord : WorkflowInstanceRecord
    {
        private string _reason;

        public WorkflowInstanceSuspendedRecord(Guid instanceId, string activityDefinitionId, string reason)
            : base(instanceId, activityDefinitionId, WorkflowInstanceStates.Suspended)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw System.Activities.Internals.FxTrace.Exception.ArgumentNullOrEmpty(nameof(reason));
            }
            this.Reason = reason;
        }

        public WorkflowInstanceSuspendedRecord(Guid instanceId, long recordNumber, string activityDefinitionId, string reason)
            : base(instanceId, recordNumber, activityDefinitionId, WorkflowInstanceStates.Suspended)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw System.Activities.Internals.FxTrace.Exception.ArgumentNullOrEmpty(nameof(reason));
            }

            this.Reason = reason;
        }

        public WorkflowInstanceSuspendedRecord(Guid instanceId, string activityDefinitionId, string reason, WorkflowIdentity workflowDefinitionIdentity)
            : this(instanceId, activityDefinitionId, reason)
        {
            this.WorkflowDefinitionIdentity = workflowDefinitionIdentity;
        }

        public WorkflowInstanceSuspendedRecord(Guid instanceId, long recordNumber, string activityDefinitionId, string reason, WorkflowIdentity workflowDefinitionIdentity)
            : this(instanceId, recordNumber, activityDefinitionId, reason)
        {
            this.WorkflowDefinitionIdentity = workflowDefinitionIdentity;
        }

        private WorkflowInstanceSuspendedRecord(WorkflowInstanceSuspendedRecord record)
            : base(record)
        {
            this.Reason = record.Reason;
        }

        public string Reason
        {
            get
            {
                return _reason;
            }
            private set
            {
                _reason = value;
            }
        }

        [DataMember(Name = "Reason")]
        internal string SerializedReason
        {
            get { return this.Reason; }
            set { this.Reason = value; }
        }

        protected internal override TrackingRecord Clone()
        {
            return new WorkflowInstanceSuspendedRecord(this);
        }

        public override string ToString()
        {
            // For backward compatibility, the ToString() does not return 
            // WorkflowIdentity, if it is null.
            if (this.WorkflowDefinitionIdentity == null)
            {
                return string.Format(CultureInfo.CurrentCulture,
                "WorkflowInstanceSuspendedRecord {{ InstanceId = {0}, RecordNumber = {1}, EventTime = {2}, ActivityDefinitionId = {3}, Reason = {4} }} ",
                this.InstanceId,
                this.RecordNumber,
                this.EventTime,
                this.ActivityDefinitionId,
                this.Reason);
            }
            else
            {
                return string.Format(CultureInfo.CurrentCulture,
                "WorkflowInstanceSuspendedRecord {{ InstanceId = {0}, RecordNumber = {1}, EventTime = {2}, ActivityDefinitionId = {3}, Reason = {4}, WorkflowDefinitionIdentity = {5} }} ",
                this.InstanceId,
                this.RecordNumber,
                this.EventTime,
                this.ActivityDefinitionId,
                this.Reason,
                this.WorkflowDefinitionIdentity);
            }
        }
    }
}
