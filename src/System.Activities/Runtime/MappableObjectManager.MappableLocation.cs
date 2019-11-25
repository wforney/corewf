// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    using System.Globalization;
    using System.Runtime.Serialization;

    internal partial class MappableObjectManager
    {
        /// <summary>
        /// The MappableLocation class.
        /// </summary>
        [DataContract]
        internal class MappableLocation
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MappableLocation" /> class.
            /// </summary>
            /// <param name="locationOwner">The location owner.</param>
            /// <param name="activity">The activity.</param>
            /// <param name="activityInstance">The activity instance.</param>
            /// <param name="location">The location.</param>
            public MappableLocation(LocationReference locationOwner, Activity activity, ActivityInstance activityInstance, Location? location)
            {
                this.Name = locationOwner.Name;
                this.OwnerDisplayName = activity.DisplayName;
                this.Location = location;
                this.MappingKeyName = string.Format(CultureInfo.InvariantCulture, "activity.{0}-{1}_{2}", activity.Id, locationOwner.Id, activityInstance.Id);
            }

            /// <summary>
            /// Gets the name.
            /// </summary>
            /// <value>The name.</value>
            public string Name { get; private set; }

            /// <summary>
            /// Gets the display name of the owner.
            /// </summary>
            /// <value>The display name of the owner.</value>
            public string OwnerDisplayName { get; private set; }

            /// <summary>
            /// Gets the location.
            /// </summary>
            /// <value>The location.</value>
            internal Location? Location { get; private set; }

            /// <summary>
            /// Gets the name of the mapping key.
            /// </summary>
            /// <value>The name of the mapping key.</value>
            internal string MappingKeyName { get; private set; }

            /// <summary>
            /// Gets or sets the serialized location.
            /// </summary>
            /// <value>The serialized location.</value>
            [DataMember(Name = "Location")]
            internal Location? SerializedLocation
            {
                get => this.Location;
                set => this.Location = value;
            }

            /// <summary>
            /// Gets or sets the name of the serialized mapping key.
            /// </summary>
            /// <value>The name of the serialized mapping key.</value>
            [DataMember(Name = "MappingKeyName")]
            internal string SerializedMappingKeyName
            {
                get => this.MappingKeyName;
                set => this.MappingKeyName = value;
            }

            /// <summary>
            /// Gets or sets the name of the serialized.
            /// </summary>
            /// <value>The name of the serialized.</value>
            [DataMember(Name = "Name")]
            internal string SerializedName
            {
                get => this.Name;
                set => this.Name = value;
            }

            /// <summary>
            /// Gets or sets the display name of the serialized owner.
            /// </summary>
            /// <value>The display name of the serialized owner.</value>
            [DataMember(EmitDefaultValue = false, Name = "OwnerDisplayName")]
            internal string SerializedOwnerDisplayName
            {
                get => this.OwnerDisplayName;
                set => this.OwnerDisplayName = value;
            }
        }
    }
}
