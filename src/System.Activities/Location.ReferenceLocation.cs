// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System;
    using System.Runtime.Serialization;

    public abstract partial class Location
    {
        /// <summary>
        /// The ReferenceLocation class.
        /// Implements the <see cref="System.Activities.Location" />
        /// </summary>
        /// <seealso cref="System.Activities.Location" />
        [DataContract]
        internal class ReferenceLocation : Location
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ReferenceLocation"/> class.
            /// </summary>
            /// <param name="innerLocation">The inner location.</param>
            /// <param name="bufferGets">if set to <c>true</c> [buffer gets].</param>
            public ReferenceLocation(Location innerLocation, bool bufferGets)
            {
                this.SerializedInnerLocation = innerLocation;
                this.SerializedBufferGets = bufferGets;
            }

            /// <summary>
            /// Gets the type of the location.
            /// </summary>
            /// <value>The type of the location.</value>
            public override Type LocationType => this.SerializedInnerLocation.LocationType;

            /// <summary>
            /// Gets or sets the value core.
            /// </summary>
            /// <value>The value core.</value>
            protected override object? ValueCore
            {
                get => this.SerializedBufferGets ? this.SerializedBufferedValue : this.SerializedInnerLocation.Value;
                set
                {
                    this.SerializedInnerLocation.Value = value;
                    this.SerializedBufferedValue = value;
                }
            }

            /// <summary>
            /// Gets or sets the serialized inner location.
            /// </summary>
            /// <value>The serialized inner location.</value>
            [DataMember(Name = "innerLocation")]
            internal Location SerializedInnerLocation { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether [serialized buffer gets].
            /// </summary>
            /// <value><c>true</c> if [serialized buffer gets]; otherwise, <c>false</c>.</value>
            [DataMember(EmitDefaultValue = false, Name = "bufferGets")]
            internal bool SerializedBufferGets { get; set; }

            /// <summary>
            /// Gets or sets the serialized buffered value.
            /// </summary>
            /// <value>The serialized buffered value.</value>
            [DataMember(EmitDefaultValue = false, Name = "bufferedValue")]
            internal object? SerializedBufferedValue { get; set; }

            /// <summary>
            /// Returns a <see cref="System.String" /> that represents this instance.
            /// </summary>
            /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
            public override string ToString() => this.SerializedBufferGets ? base.ToString() : this.SerializedInnerLocation.ToString();
        }
    }
}
