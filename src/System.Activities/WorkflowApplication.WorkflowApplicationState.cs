// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    public sealed partial class WorkflowApplication
    {
        /// <summary>
        /// The WorkflowApplicationState enumeration.
        /// </summary>
        private enum WorkflowApplicationState : byte
        {
            /// <summary>
            /// The paused
            /// </summary>
            Paused,
            /// <summary>
            /// The runnable
            /// </summary>
            Runnable,
            /// <summary>
            /// The unloaded
            /// </summary>
            Unloaded,
            /// <summary>
            /// The aborted
            /// </summary>
            Aborted
        }
    }
}