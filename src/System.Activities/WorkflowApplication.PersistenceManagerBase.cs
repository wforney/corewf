// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime.DurableInstancing;

    public sealed partial class WorkflowApplication
    {
        /// <summary>
        /// The PersistenceManagerBase class.
        /// </summary>
        /// <remarks>
        /// This is a thin shell of PersistenceManager functionality so that
        /// WorkflowApplicationInstance can hold onto a PM without exposing the entire persistence functionality
        /// </remarks>
        internal abstract class PersistenceManagerBase
        {
            /// <summary>
            /// Gets the instance identifier.
            /// </summary>
            /// <value>The instance identifier.</value>
            public abstract Guid InstanceId { get; }
            /// <summary>
            /// Gets the instance store.
            /// </summary>
            /// <value>The instance store.</value>
            public abstract InstanceStore InstanceStore { get; }
        }
    }
}