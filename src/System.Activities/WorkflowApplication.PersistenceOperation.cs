// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    public sealed partial class WorkflowApplication
    {
        /// <summary>
        /// The PersistenceOperation enumeration.
        /// </summary>
        private enum PersistenceOperation : byte
        {
            /// <summary>
            /// The complete
            /// </summary>
            Complete,

            /// <summary>
            /// The save
            /// </summary>
            Save,

            /// <summary>
            /// The unload
            /// </summary>
            Unload
        }
    }
}