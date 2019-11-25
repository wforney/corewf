// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Runtime.Serialization;

    public partial class Location<T>
    {
        /// <summary>
        /// The ReferenceLocation class.
        /// Implements the <see cref="System.Activities.Location{T}" />
        /// </summary>
        /// <seealso cref="System.Activities.Location{T}" />
        [DataContract]
        internal new class ReferenceLocation : Location<T>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ReferenceLocation"/> class.
            /// </summary>
            /// <param name="innerLocation">The inner location.</param>
            /// <param name="bufferGets">if set to <c>true</c> [buffer gets].</param>
            public ReferenceLocation(Location<T> innerLocation, bool bufferGets)
            {
                this.SerializedInnerLocation = innerLocation;
                this.SerializedBufferGets = bufferGets;
            }

            /// <summary>
            /// Gets or sets the value.
            /// </summary>
            /// <value>The value.</value>
            public override T Value
            {
                get
                {
                    if (this.SerializedBufferGets)
                    {
                        return this.SerializedValue;
                    }
                    else
                    {
                        return this.SerializedInnerLocation.Value;
                    }
                }
                set
                {
                    this.SerializedInnerLocation.Value = value;

                    if (this.SerializedBufferGets)
                    {
                        this.SerializedValue = value;
                    }
                }
            }

            /// <summary>
            /// Gets or sets the serialized inner location.
            /// </summary>
            /// <value>The serialized inner location.</value>
            [DataMember(Name = "innerLocation")]
            internal Location<T> SerializedInnerLocation { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether [serialized buffer gets].
            /// </summary>
            /// <value><c>true</c> if [serialized buffer gets]; otherwise, <c>false</c>.</value>
            [DataMember(EmitDefaultValue = false, Name = "bufferGets")]
            internal bool SerializedBufferGets { get; set; }

            /// <summary>
            /// Returns a <see cref="System.String" /> that represents this instance.
            /// </summary>
            /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
            public override string ToString() =>
                this.SerializedBufferGets ? base.ToString() : this.SerializedInnerLocation.ToString();
        }
    }
}
