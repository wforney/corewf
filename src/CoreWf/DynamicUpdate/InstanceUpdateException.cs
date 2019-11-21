// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.DynamicUpdate
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Security;
    using System.Runtime;
    using System.Activities.Runtime;

    [Serializable]
    public class InstanceUpdateException : Exception
    {
        private ReadOnlyCollection<ActivityBlockingUpdate> blockingActivities;

        public InstanceUpdateException()
            : base()
        {
        }

        public InstanceUpdateException(string message)
            : base(message)
        {
        }

        public InstanceUpdateException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public InstanceUpdateException(IList<ActivityBlockingUpdate> blockingActivities)
            : this(BuildMessage(blockingActivities), blockingActivities)
        {
        }

        public InstanceUpdateException(string message, IList<ActivityBlockingUpdate> blockingActivities)
            : base(message)
        {
            if (blockingActivities != null)
            {
                this.blockingActivities = new ReadOnlyCollection<ActivityBlockingUpdate>(blockingActivities);
            }
        }

        public InstanceUpdateException(string message, IList<ActivityBlockingUpdate> blockingActivities, Exception innerException)
            : base(message, innerException)
        {
            if (blockingActivities != null)
            {
                this.blockingActivities = new ReadOnlyCollection<ActivityBlockingUpdate>(blockingActivities);
            }
        }

        protected InstanceUpdateException(SerializationInfo info, StreamingContext context)
            : base(info, context) => this.blockingActivities = (ReadOnlyCollection<ActivityBlockingUpdate>)info.GetValue(
                "blockingActivities", typeof(ReadOnlyCollection<ActivityBlockingUpdate>));

        public IList<ActivityBlockingUpdate> BlockingActivities
        {
            get
            {
                if (this.blockingActivities == null)
                {
                    this.blockingActivities = new ReadOnlyCollection<ActivityBlockingUpdate>(Array.Empty<ActivityBlockingUpdate>());
                }

                return this.blockingActivities;
            }
        }

        [Fx.Tag.SecurityNote(Critical = "Critical because we are overriding a critical method in the base class.")]
        [SecurityCritical]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("blockingActivities", this.blockingActivities);
        }

        private static string BuildMessage(IList<ActivityBlockingUpdate> blockingActivities)
        {
            if (blockingActivities != null && blockingActivities.Count > 0)
            {
                var errorMsgs = new StringBuilder();
                for (var i = 0; i < blockingActivities.Count - 1; i++)
                {
                    errorMsgs.AppendLine(GetMessage(blockingActivities[i]));
                }

                errorMsgs.Append(GetMessage(blockingActivities[blockingActivities.Count - 1]));
                return errorMsgs.ToString();
            }

            return null;
        }

        private static string GetMessage(ActivityBlockingUpdate blockingActivity)
        {
            var activity = (object)blockingActivity.Activity ?? blockingActivity.UpdatedActivityId;
            if (activity != null)
            {
                return SR.ActivityBlockingUpdate(activity, blockingActivity.Reason);
            }
            else
            {
                return blockingActivity.Reason;
            }
        }
    }    
}
