// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Hosting
{
    using System.Activities.Runtime;
    using System.Runtime.Serialization;

    /// <summary>
    /// The LocationInfo class. This class cannot be inherited.
    /// </summary>
    [DataContract]
    [Fx.Tag.XamlVisible(false)]
    public sealed class LocationInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LocationInfo"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="ownerDisplayName">Display name of the owner.</param>
        /// <param name="value">The value.</param>
        internal LocationInfo(string name, string ownerDisplayName, object? value)
        {
            this.Name = name;
            this.OwnerDisplayName = ownerDisplayName;
            this.Value = value;
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
        /// Gets the value.
        /// </summary>
        /// <value>The value.</value>
        public object? Value { get; private set; }

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

        /// <summary>
        /// Gets or sets the serialized value.
        /// </summary>
        /// <value>The serialized value.</value>
        [DataMember(EmitDefaultValue = false, Name = "Value")]
        internal object? SerializedValue
        {
            get => this.Value;
            set => this.Value = value;
        }
    }
}
