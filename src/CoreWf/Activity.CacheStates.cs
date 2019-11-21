// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    public abstract partial class Activity
    {
        /// <summary>
        /// The CacheStates enumeration.
        /// </summary>
        private enum CacheStates : byte
        {
            /// <summary>
            /// We don't have valid cached data
            /// </summary>
            Uncached = 0x00,

            // The next two states are mutually exclusive:

            /// <summary>
            /// The activity has its own metadata cached, or private implementation are skipped
            /// </summary>
            Partial = 0x01,

            /// <summary>
            /// The activity has its own metadata and its private implementation cached
            /// We can make use of the roll-up metadata (like SubtreeHasConstraints).
            /// </summary>
            Full = 0x02,

            // The next state can be ORed with the last two:

            /// <summary>
            /// The cached data is ready for runtime use
            /// </summary>
            RuntimeReady = 0x04
        }
    }
}