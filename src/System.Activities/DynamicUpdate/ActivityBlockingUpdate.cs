// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.DynamicUpdate
{
    using System;
    using System.Collections.ObjectModel;
    using System.Runtime.Serialization;

    [Serializable]
    [DataContract]
    public class ActivityBlockingUpdate
    {
        [NonSerialized]
        private readonly Activity activity;
        [NonSerialized]
        private string activityInstanceId;
        [NonSerialized]
        private string originalActivityId;
        [NonSerialized]
        private string updatedActivityId;
        [NonSerialized]
        private string reason;       

        public ActivityBlockingUpdate(Activity activity, string originalActivityId, string reason)
            : this(activity, originalActivityId, reason, null)
        {
        }

        public ActivityBlockingUpdate(Activity activity, string originalActivityId, string reason, string activityInstanceId)
        {
            this.activity = activity;
            this.Reason = reason;
            this.OriginalActivityId = originalActivityId;
            this.ActivityInstanceId = activityInstanceId;
            if (activity != null)
            {
                this.UpdatedActivityId = activity.Id;
            }
        }

        public ActivityBlockingUpdate(string updatedActivityId, string originalActivityId, string reason)
            : this(updatedActivityId, originalActivityId, reason, null)
        {
        }

        public ActivityBlockingUpdate(string updatedActivityId, string originalActivityId, string reason, string activityInstanceId)
        {
            this.UpdatedActivityId = updatedActivityId;
            this.OriginalActivityId = originalActivityId;
            this.ActivityInstanceId = activityInstanceId;
            this.Reason = reason;
        }

        public Activity Activity => this.activity;

        public string ActivityInstanceId
        {
            get => this.activityInstanceId;
            private set => this.activityInstanceId = value;
        }

        public string OriginalActivityId
        {
            get => this.originalActivityId;
            private set => this.originalActivityId = value;
        }

        public string UpdatedActivityId
        {
            get => this.updatedActivityId;
            private set => this.updatedActivityId = value;
        }

        public string Reason
        {
            get => this.reason;
            private set => this.reason = value;
        }

        [DataMember(EmitDefaultValue = false, Name = "ActivityInstanceId")]
        internal string SerializedActivityInstanceId
        {
            get => this.ActivityInstanceId;
            set => this.ActivityInstanceId = value;
        }

        [DataMember(EmitDefaultValue = false, Name = "OriginalActivityId")]
        internal string SerializedOriginalActivityId
        {
            get => this.OriginalActivityId;
            set => this.OriginalActivityId = value;
        }

        [DataMember(EmitDefaultValue = false, Name = "UpdatedActivityId")]
        internal string SerializedUpdatedActivityId
        {
            get => this.UpdatedActivityId;
            set => this.UpdatedActivityId = value;
        }

        [DataMember(EmitDefaultValue = false, Name = "Reason")]
        internal string SerializedReason
        {
            get => this.Reason;
            set => this.Reason = value;
        }

        internal static void AddBlockingActivity(ref Collection<ActivityBlockingUpdate> blockingActivities, Activity activity, string originalActivityId, string reason, string activityInstanceId)
        {
            if (blockingActivities == null)
            {
                blockingActivities = new Collection<ActivityBlockingUpdate>();
            }

            var blockingActivity = new ActivityBlockingUpdate(activity, originalActivityId, reason, activityInstanceId);
            blockingActivities.Add(blockingActivity);
        }

        internal static void AddBlockingActivity(ref Collection<ActivityBlockingUpdate> blockingActivities, string updatedActivityId, string originalActivityId, string reason, string activityInstanceId)
        {
            if (blockingActivities == null)
            {
                blockingActivities = new Collection<ActivityBlockingUpdate>();
            }

            var blockingActivity = new ActivityBlockingUpdate(updatedActivityId, originalActivityId, reason, activityInstanceId);
            blockingActivities.Add(blockingActivity);
        }
    }
}
