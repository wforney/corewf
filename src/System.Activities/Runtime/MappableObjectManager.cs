// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    using System.Activities.Hosting;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    /// <summary>
    /// The MappableObjectManager class.
    /// </summary>
    [DataContract]
    internal partial class MappableObjectManager
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MappableObjectManager"/> class.
        /// </summary>
        public MappableObjectManager()
        {
        }

        /// <summary>
        /// Gets the count.
        /// </summary>
        /// <value>The count.</value>
        public int Count
        {
            get
            {
                var result = 0;
                if (this.SerializedMappableLocations != null)
                {
                    result += this.SerializedMappableLocations.Count;
                }

                return result;
            }
        }

        /// <summary>
        /// Gets or sets the serialized mappable locations.
        /// </summary>
        /// <value>The serialized mappable locations.</value>
        [DataMember(EmitDefaultValue = false, Name = "mappableLocations")]
        internal List<MappableLocation>? SerializedMappableLocations { get; set; }

        /// <summary>
        /// Gathers the mappable variables.
        /// </summary>
        /// <returns>System.Nullable&lt;IDictionary&lt;System.String, LocationInfo&gt;&gt;.</returns>
        public IDictionary<string, LocationInfo>? GatherMappableVariables()
        {
            Dictionary<string, LocationInfo>? result = null;
            if (this.SerializedMappableLocations != null && this.SerializedMappableLocations.Count > 0)
            {
                result = new Dictionary<string, LocationInfo>(this.SerializedMappableLocations.Count);
                for (var locationIndex = 0; locationIndex < this.SerializedMappableLocations.Count; locationIndex++)
                {
                    var mappableLocation = this.SerializedMappableLocations[locationIndex];
                    result.Add(
                        mappableLocation.MappingKeyName,
                        new LocationInfo(
                            mappableLocation.Name,
                            mappableLocation.OwnerDisplayName,
                            mappableLocation.Location?.Value));
                }
            }

            return result;
        }

        /// <summary>
        /// Registers the specified location.
        /// </summary>
        /// <param name="location">The location.</param>
        /// <param name="activity">The activity.</param>
        /// <param name="locationOwner">The location owner.</param>
        /// <param name="activityInstance">The activity instance.</param>
        public void Register(Location? location, Activity activity, LocationReference locationOwner, ActivityInstance activityInstance)
        {
            Fx.Assert(location?.CanBeMapped ?? false, "should only register mappable locations");

            if (this.SerializedMappableLocations == null)
            {
                this.SerializedMappableLocations = new List<MappableLocation>();
            }

            this.SerializedMappableLocations.Add(new MappableLocation(locationOwner, activity, activityInstance, location));
        }

        /// <summary>
        /// Unregisters the specified location.
        /// </summary>
        /// <param name="location">The location.</param>
        public void Unregister(Location location)
        {
            Fx.Assert(location.CanBeMapped, "should only register mappable locations");

            var mappedLocationsCount = this.SerializedMappableLocations?.Count;
            for (var i = 0; i < mappedLocationsCount; i++)
            {
                if (object.ReferenceEquals(this.SerializedMappableLocations?[i].Location, location))
                {
                    this.SerializedMappableLocations.RemoveAt(i);
                    break;
                }
            }

            Fx.Assert(this.SerializedMappableLocations?.Count == mappedLocationsCount - 1, "can only unregister locations that have been registered");
        }
    }
}
