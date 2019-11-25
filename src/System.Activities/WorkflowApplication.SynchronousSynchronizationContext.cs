// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Threading;

    public sealed partial class WorkflowApplication
    {
        /// <summary>
        /// The SynchronousSynchronizationContext class.
        /// Implements the <see cref="System.Threading.SynchronizationContext" />
        /// </summary>
        /// <seealso cref="System.Threading.SynchronizationContext" />
        internal class SynchronousSynchronizationContext : SynchronizationContext
        {
            /// <summary>
            /// The value
            /// </summary>
            private static SynchronousSynchronizationContext value;

            /// <summary>
            /// Prevents a default instance of the <see cref="SynchronousSynchronizationContext"/> class from being created.
            /// </summary>
            private SynchronousSynchronizationContext()
            {
            }

            /// <summary>
            /// Gets the value.
            /// </summary>
            /// <value>The value.</value>
            public static SynchronousSynchronizationContext Value
            {
                get
                {
                    if (value == null)
                    {
                        value = new SynchronousSynchronizationContext();
                    }

                    return value;
                }
            }

            /// <summary>
            /// When overridden in a derived class, dispatches an asynchronous message to a synchronization context.
            /// </summary>
            /// <param name="d">The <see cref="T:System.Threading.SendOrPostCallback" /> delegate to call.</param>
            /// <param name="state">The object passed to the delegate.</param>
            public override void Post(SendOrPostCallback d, object state) => d(state);

            /// <summary>
            /// When overridden in a derived class, dispatches a synchronous message to a synchronization context.
            /// </summary>
            /// <param name="d">The <see cref="T:System.Threading.SendOrPostCallback" /> delegate to call.</param>
            /// <param name="state">The object passed to the delegate.</param>
            public override void Send(SendOrPostCallback d, object state) => d(state);
        }
    }
}