// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.EtwTracking
{
    using System.Activities.Tracking;
    using System.Diagnostics.Tracing;
    using System.Text.Json;

    /// <summary>
    /// The EtwTrackingParticipant class. This class cannot be inherited.
    /// Implements the <see cref="System.Activities.Tracking.TrackingParticipant" />
    /// </summary>
    /// <seealso cref="System.Activities.Tracking.TrackingParticipant" />
    public sealed class EtwTrackingParticipant : TrackingParticipant
    {
        /// <summary>
        /// The truncated items tag
        /// </summary>
        private const string truncatedItemsTag = "<items>...</items>";

        /// <summary>
        /// The empty items tag
        /// </summary>
        private const string emptyItemsTag = "<items />";

        /// <summary>
        /// The items tag
        /// </summary>
        private const string itemsTag = "items";

        /// <summary>
        /// The item tag
        /// </summary>
        private const string itemTag = "item";

        /// <summary>
        /// The name attribute
        /// </summary>
        private const string nameAttribute = "name";

        /// <summary>
        /// The type attribute
        /// </summary>
        private const string typeAttribute = "type";

        /// <summary>
        /// Initializes a new instance of the <see cref="EtwTrackingParticipant" /> class.
        /// </summary>
        public EtwTrackingParticipant() => this.ApplicationReference = string.Empty;

        /// <summary>
        /// Gets or sets the application reference.
        /// </summary>
        /// <value>The application reference.</value>
        public string ApplicationReference { get; set; }

        /// <summary>
        /// Begins the track.
        /// </summary>
        /// <param name="record">The record.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns>IAsyncResult.</returns>
        protected override IAsyncResult BeginTrack(TrackingRecord record, TimeSpan timeout, AsyncCallback callback, object state)
        {
            this.Track(record, timeout);
            return new EtwTrackingAsyncResult(callback, state);
        }

        /// <summary>
        /// Ends the track.
        /// </summary>
        /// <param name="result">The result.</param>
        protected override void EndTrack(IAsyncResult result)
        {
            EtwTrackingAsyncResult.End(result as EtwTrackingAsyncResult);
        }

        /// <summary>
        /// Tracks the specified record.
        /// </summary>
        /// <param name="record">The record.</param>
        /// <param name="timeout">The timeout.</param>
        /// <exception cref="PlatformNotSupportedException"></exception>
        protected override void Track(TrackingRecord record, TimeSpan timeout)
        {
            if (record is ActivityStateRecord)
            {
                this.TrackActivityRecord((ActivityStateRecord)record);
            }
            else if (record is WorkflowInstanceRecord)
            {
                this.TrackWorkflowRecord((WorkflowInstanceRecord)record);
            }
            else if (record is BookmarkResumptionRecord)
            {
                this.TrackBookmarkRecord((BookmarkResumptionRecord)record);
            }
            else if (record is ActivityScheduledRecord)
            {
                this.TrackActivityScheduledRecord((ActivityScheduledRecord)record);
            }
            else if (record is CancelRequestedRecord)
            {
                this.TrackCancelRequestedRecord((CancelRequestedRecord)record);
            }
            else if (record is FaultPropagationRecord)
            {
                this.TrackFaultPropagationRecord((FaultPropagationRecord)record);
            }
            else if (record is CustomTrackingRecord)
            {
                this.TrackCustomRecord((CustomTrackingRecord)record);
            }
            else
            {
                throw new PlatformNotSupportedException(Resources.UnrecognizedTrackingRecord(record?.GetType().Name));
            }
        }

        /// <summary>
        /// Tracks the activity record.
        /// </summary>
        /// <param name="record">The record.</param>
        private void TrackActivityRecord(ActivityStateRecord record)
        {
            if (WfEtwTrackingEventSource.Instance.ActivityStateRecordIsEnabled())
            {
                WfEtwTrackingEventSource.Instance.ActivityStateRecord(
                    record.InstanceId,
                    record.RecordNumber,
                    record.EventTime,
                    record.State,
                    record.Activity.Name,
                    record.Activity.Id,
                    record.Activity.InstanceId,
                    record.Activity.TypeName,
                    record.Arguments.Count > 0 ? JsonSerializer.Serialize(record.Arguments, new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                    record.Variables.Count > 0 ? JsonSerializer.Serialize(record.Variables, new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                    record.HasAnnotations ? JsonSerializer.Serialize(record.Annotations, new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                    this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
            }
        }

        /// <summary>
        /// Tracks the activity scheduled record.
        /// </summary>
        /// <param name="scheduledRecord">The scheduled record.</param>
        private void TrackActivityScheduledRecord(ActivityScheduledRecord scheduledRecord)
        {
            if (WfEtwTrackingEventSource.Instance.ActivityScheduledRecordIsEnabled())
            {
                WfEtwTrackingEventSource.Instance.ActivityScheduledRecord(scheduledRecord.InstanceId,
                    scheduledRecord.RecordNumber,
                    scheduledRecord.EventTime,
                    scheduledRecord.Activity == null ? string.Empty : scheduledRecord.Activity.Name,
                    scheduledRecord.Activity == null ? string.Empty : scheduledRecord.Activity.Id,
                    scheduledRecord.Activity == null ? string.Empty : scheduledRecord.Activity.InstanceId,
                    scheduledRecord.Activity == null ? string.Empty : scheduledRecord.Activity.TypeName,
                    scheduledRecord.Child.Name, scheduledRecord.Child.Id, scheduledRecord.Child.InstanceId, scheduledRecord.Child.TypeName,
                    scheduledRecord.HasAnnotations ? JsonSerializer.Serialize(scheduledRecord.Annotations, new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                    this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
            }
        }

        /// <summary>
        /// Tracks the cancel requested record.
        /// </summary>
        /// <param name="cancelRecord">The cancel record.</param>
        private void TrackCancelRequestedRecord(CancelRequestedRecord cancelRecord)
        {
            if (WfEtwTrackingEventSource.Instance.CancelRequestedRecordIsEnabled())
            {
                WfEtwTrackingEventSource.Instance.CancelRequestedRecord(cancelRecord.InstanceId,
                    cancelRecord.RecordNumber,
                    cancelRecord.EventTime,
                    cancelRecord.Activity == null ? string.Empty : cancelRecord.Activity.Name,
                    cancelRecord.Activity == null ? string.Empty : cancelRecord.Activity.Id,
                    cancelRecord.Activity == null ? string.Empty : cancelRecord.Activity.InstanceId,
                    cancelRecord.Activity == null ? string.Empty : cancelRecord.Activity.TypeName,
                    cancelRecord.Child.Name, cancelRecord.Child.Id, cancelRecord.Child.InstanceId, cancelRecord.Child.TypeName,
                    cancelRecord.HasAnnotations ? JsonSerializer.Serialize(cancelRecord.Annotations, new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                    this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
            }
        }

        /// <summary>
        /// Tracks the fault propagation record.
        /// </summary>
        /// <param name="faultRecord">The fault record.</param>
        private void TrackFaultPropagationRecord(FaultPropagationRecord faultRecord)
        {
            if (WfEtwTrackingEventSource.Instance.FaultPropagationRecordIsEnabled())
            {
                WfEtwTrackingEventSource.Instance.FaultPropagationRecord(faultRecord.InstanceId,
                    faultRecord.RecordNumber,
                    faultRecord.EventTime,
                    faultRecord.FaultSource.Name, faultRecord.FaultSource.Id, faultRecord.FaultSource.InstanceId, faultRecord.FaultSource.TypeName,
                    faultRecord.FaultHandler != null ? faultRecord.FaultHandler.Name : string.Empty,
                    faultRecord.FaultHandler != null ? faultRecord.FaultHandler.Id : string.Empty,
                    faultRecord.FaultHandler != null ? faultRecord.FaultHandler.InstanceId : string.Empty,
                    faultRecord.FaultHandler != null ? faultRecord.FaultHandler.TypeName : string.Empty,
                    faultRecord.Fault.ToString(), faultRecord.IsFaultSource,
                    faultRecord.HasAnnotations ? JsonSerializer.Serialize(faultRecord.Annotations, new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                    this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
            }
        }

        /// <summary>
        /// Tracks the bookmark record.
        /// </summary>
        /// <param name="record">The record.</param>
        private void TrackBookmarkRecord(BookmarkResumptionRecord record)
        {
            if (WfEtwTrackingEventSource.Instance.BookmarkResumptionRecordIsEnabled())
            {
                WfEtwTrackingEventSource.Instance.BookmarkResumptionRecord(record.InstanceId, record.RecordNumber, record.EventTime,
                    record.BookmarkName, record.BookmarkScope, record.Owner.Name, record.Owner.Id,
                    record.Owner.InstanceId, record.Owner.TypeName,
                    record.HasAnnotations ? JsonSerializer.Serialize(record.Annotations, new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                    this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
            }
        }

        /// <summary>
        /// Tracks the custom record.
        /// </summary>
        /// <param name="record">The record.</param>
        private void TrackCustomRecord(CustomTrackingRecord record)
        {
            switch (record.Level)
            {
                case EventLevel.Error:
                    if (WfEtwTrackingEventSource.Instance.CustomTrackingRecordErrorIsEnabled())
                    {
                        WfEtwTrackingEventSource.Instance.CustomTrackingRecordError(record.InstanceId,
                            record.RecordNumber, record.EventTime, record.Name,
                            record.Activity.Name, record.Activity.Id, record.Activity.InstanceId, record.Activity.TypeName,
                            JsonSerializer.Serialize(record.Data,  new JsonSerializerOptions { WriteIndented = true }),
                            record.HasAnnotations ? JsonSerializer.Serialize(record.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                            this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
                    }
                    break;
                case EventLevel.Warning:
                    if (WfEtwTrackingEventSource.Instance.CustomTrackingRecordWarningIsEnabled())
                    {
                        WfEtwTrackingEventSource.Instance.CustomTrackingRecordWarning(record.InstanceId,
                            record.RecordNumber, record.EventTime, record.Name,
                            record.Activity.Name, record.Activity.Id, record.Activity.InstanceId, record.Activity.TypeName,
                            JsonSerializer.Serialize(record.Data,  new JsonSerializerOptions { WriteIndented = true }),
                            record.HasAnnotations ? JsonSerializer.Serialize(record.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                            this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
                    }
                    break;

                default:
                    if (WfEtwTrackingEventSource.Instance.CustomTrackingRecordInfoIsEnabled())
                    {
                        WfEtwTrackingEventSource.Instance.CustomTrackingRecordInfo(record.InstanceId,
                            record.RecordNumber, record.EventTime, record.Name,
                            record.Activity.Name, record.Activity.Id, record.Activity.InstanceId, record.Activity.TypeName,
                            JsonSerializer.Serialize(record.Data,  new JsonSerializerOptions { WriteIndented = true }),
                            record.HasAnnotations ? JsonSerializer.Serialize(record.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                            this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
                    }
                    break;
            }
        }

        /// <summary>
        /// Tracks the workflow record.
        /// </summary>
        /// <param name="record">The record.</param>
        private void TrackWorkflowRecord(WorkflowInstanceRecord record)
        {
            // In the TrackWorkflowInstance*Record methods below there are two code paths.
            // If the WorkflowIdentity is null, then we follow the exisiting 4.0 path.
            // If the WorkflowIdentity is provided, then if a particular field in the workflowInstance 
            // record is null, we need to ensure that we are passing string.Empty.
            // The WriteEvent method on the DiagnosticEventProvider which is called in the 
            // WriteEtwEvent in the EtwTrackingParticipantRecords class invokes the EventWrite
            // native method which relies on getting the record arguments in a particular order.
            if (record is WorkflowInstanceUnhandledExceptionRecord)
            {
                this.TrackWorkflowInstanceUnhandledExceptionRecord(record);
            }
            else if (record is WorkflowInstanceAbortedRecord)
            {
                this.TrackWorkflowInstanceAbortedRecord(record);
            }
            else if (record is WorkflowInstanceSuspendedRecord)
            {
                this.TrackWorkflowInstanceSuspendedRecord(record);
            }
            else if (record is WorkflowInstanceTerminatedRecord)
            {
                this.TrackWorkflowInstanceTerminatedRecord(record);
            }
            else
            {
                this.TrackWorkflowInstanceRecord(record);
            }
        }

        /// <summary>
        /// Tracks the workflow instance unhandled exception record.
        /// </summary>
        /// <param name="record">The record.</param>
        private void TrackWorkflowInstanceUnhandledExceptionRecord(WorkflowInstanceRecord record)
        {
            var unhandled = record as WorkflowInstanceUnhandledExceptionRecord;
            if (unhandled.WorkflowDefinitionIdentity == null)
            {
                if (WfEtwTrackingEventSource.Instance.WorkflowInstanceUnhandledExceptionRecordIsEnabled())
                {
                    WfEtwTrackingEventSource.Instance.WorkflowInstanceUnhandledExceptionRecord(unhandled.InstanceId,
                        unhandled.RecordNumber, unhandled.EventTime, unhandled.ActivityDefinitionId,
                        unhandled.FaultSource.Name, unhandled.FaultSource.Id, unhandled.FaultSource.InstanceId, unhandled.FaultSource.TypeName,
                        unhandled.UnhandledException == null ? string.Empty : unhandled.UnhandledException.ToString(),
                        unhandled.HasAnnotations ? JsonSerializer.Serialize(unhandled.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                        this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
                }
            }
            else
            {
                if (WfEtwTrackingEventSource.Instance.WorkflowInstanceUnhandledExceptionRecordWithIdIsEnabled())
                {
                    WfEtwTrackingEventSource.Instance.WorkflowInstanceUnhandledExceptionRecordWithId(unhandled.InstanceId,
                        unhandled.RecordNumber, unhandled.EventTime, unhandled.ActivityDefinitionId,
                        unhandled.FaultSource.Name, unhandled.FaultSource.Id, unhandled.FaultSource.InstanceId, unhandled.FaultSource.TypeName,
                        unhandled.UnhandledException == null ? string.Empty : unhandled.UnhandledException.ToString(),
                        unhandled.HasAnnotations ? JsonSerializer.Serialize(unhandled.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                        this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name ?? string.Empty,
                        unhandled.WorkflowDefinitionIdentity.ToString(), this.ApplicationReference);
                }
            }
        }

        /// <summary>
        /// Tracks the workflow instance aborted record.
        /// </summary>
        /// <param name="record">The record.</param>
        private void TrackWorkflowInstanceAbortedRecord(WorkflowInstanceRecord record)
        {
            var aborted = record as WorkflowInstanceAbortedRecord;
            if (aborted.WorkflowDefinitionIdentity == null)
            {
                if (WfEtwTrackingEventSource.Instance.WorkflowInstanceAbortedRecordIsEnabled())
                {
                    WfEtwTrackingEventSource.Instance.WorkflowInstanceAbortedRecord(aborted.InstanceId, aborted.RecordNumber,
                        aborted.EventTime, aborted.ActivityDefinitionId, aborted.Reason,
                        aborted.HasAnnotations ? JsonSerializer.Serialize(aborted.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                        this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
                }
            }
            else
            {
                if (WfEtwTrackingEventSource.Instance.WorkflowInstanceAbortedRecordWithIdIsEnabled())
                {
                    WfEtwTrackingEventSource.Instance.WorkflowInstanceAbortedRecordWithId(aborted.InstanceId, aborted.RecordNumber,
                        aborted.EventTime, aborted.ActivityDefinitionId, aborted.Reason,
                        aborted.HasAnnotations ? JsonSerializer.Serialize(aborted.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                        this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name ?? string.Empty,
                        aborted.WorkflowDefinitionIdentity.ToString(), this.ApplicationReference);
                }
            }
        }

        /// <summary>
        /// Tracks the workflow instance suspended record.
        /// </summary>
        /// <param name="record">The record.</param>
        private void TrackWorkflowInstanceSuspendedRecord(WorkflowInstanceRecord record)
        {
            var suspended = record as WorkflowInstanceSuspendedRecord;
            if (suspended.WorkflowDefinitionIdentity == null)
            {
                if (WfEtwTrackingEventSource.Instance.WorkflowInstanceSuspendedRecordIsEnabled())
                {
                    WfEtwTrackingEventSource.Instance.WorkflowInstanceSuspendedRecord(suspended.InstanceId, suspended.RecordNumber,
                        suspended.EventTime, suspended.ActivityDefinitionId, suspended.Reason,
                        suspended.HasAnnotations ? JsonSerializer.Serialize(suspended.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                        this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
                }
            }
            else
            {
                if (WfEtwTrackingEventSource.Instance.WorkflowInstanceSuspendedRecordWithIdIsEnabled())
                {
                    WfEtwTrackingEventSource.Instance.WorkflowInstanceSuspendedRecordWithId(suspended.InstanceId, suspended.RecordNumber,
                        suspended.EventTime, suspended.ActivityDefinitionId, suspended.Reason,
                        suspended.HasAnnotations ? JsonSerializer.Serialize(suspended.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                        this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name ?? string.Empty,
                        suspended.WorkflowDefinitionIdentity.ToString(), this.ApplicationReference);
                }
            }
        }

        /// <summary>
        /// Tracks the workflow instance terminated record.
        /// </summary>
        /// <param name="record">The record.</param>
        private void TrackWorkflowInstanceTerminatedRecord(WorkflowInstanceRecord record)
        {
            var terminated = record as WorkflowInstanceTerminatedRecord;
            if (terminated.WorkflowDefinitionIdentity == null)
            {
                if (WfEtwTrackingEventSource.Instance.WorkflowInstanceTerminatedRecordIsEnabled())
                {
                    WfEtwTrackingEventSource.Instance.WorkflowInstanceTerminatedRecord(terminated.InstanceId, terminated.RecordNumber,
                        terminated.EventTime, terminated.ActivityDefinitionId, terminated.Reason,
                        terminated.HasAnnotations ? JsonSerializer.Serialize(terminated.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                        this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
                }
            }
            else
            {
                if (WfEtwTrackingEventSource.Instance.WorkflowInstanceTerminatedRecordWithIdIsEnabled())
                {
                    WfEtwTrackingEventSource.Instance.WorkflowInstanceTerminatedRecordWithId(terminated.InstanceId, terminated.RecordNumber,
                        terminated.EventTime, terminated.ActivityDefinitionId, terminated.Reason,
                        terminated.HasAnnotations ? JsonSerializer.Serialize(terminated.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                        this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name ?? string.Empty,
                        terminated.WorkflowDefinitionIdentity.ToString(), this.ApplicationReference);
                }
            }
        }

        /// <summary>
        /// Tracks the workflow instance record.
        /// </summary>
        /// <param name="record">The record.</param>
        private void TrackWorkflowInstanceRecord(WorkflowInstanceRecord record)
        {
            if (record.WorkflowDefinitionIdentity == null)
            {
                if (WfEtwTrackingEventSource.Instance.WorkflowInstanceRecordIsEnabled())
                {
                    WfEtwTrackingEventSource.Instance.WorkflowInstanceRecord(record.InstanceId, record.RecordNumber,
                        record.EventTime, record.ActivityDefinitionId,
                        record.State,
                        record.HasAnnotations ? JsonSerializer.Serialize(record.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                        this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name, this.ApplicationReference);
                }
            }
            else
            {
                if (WfEtwTrackingEventSource.Instance.WorkflowInstanceRecordWithIdIsEnabled())
                {
                    WfEtwTrackingEventSource.Instance.WorkflowInstanceRecordWithId(record.InstanceId, record.RecordNumber,
                        record.EventTime, record.ActivityDefinitionId,
                        record.State,
                        record.HasAnnotations ? JsonSerializer.Serialize(record.Annotations,  new JsonSerializerOptions { WriteIndented = true }) : emptyItemsTag,
                        this.TrackingProfile == null ? string.Empty : this.TrackingProfile.Name ?? string.Empty,
                        record.WorkflowDefinitionIdentity.ToString(), this.ApplicationReference);
                }
            }
        }
    }
}
