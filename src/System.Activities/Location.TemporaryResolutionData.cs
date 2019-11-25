// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Activities.Runtime;
    using System.Runtime.Serialization;

    public abstract partial class Location
    {
        /// <summary>
        /// The TemporaryResolutionData structure.
        /// </summary>
        [DataContract]
        internal struct TemporaryResolutionData
        {
            /// <summary>
            /// Gets or sets the temporary resolution environment.
            /// </summary>
            /// <value>The temporary resolution environment.</value>
            [DataMember(EmitDefaultValue = false)]
            public LocationEnvironment TemporaryResolutionEnvironment { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether [buffer gets on collapse].
            /// </summary>
            /// <value><c>true</c> if [buffer gets on collapse]; otherwise, <c>false</c>.</value>
            [DataMember(EmitDefaultValue = false)]
            public bool BufferGetsOnCollapse { get; set; }
        }
    }
}
