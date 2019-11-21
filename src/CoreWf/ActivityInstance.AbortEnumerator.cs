// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System;
    using System.Activities.Runtime;
    using System.Collections;
    using System.Collections.Generic;

    public sealed partial class ActivityInstance

#if NET45
#else
#endif
    {
        /// <summary>
        /// The AbortEnumerator class.
        /// Implements the <see cref="System.Collections.Generic.IEnumerator{System.Activities.ActivityInstance}" />
        /// </summary>
        /// <seealso cref="System.Collections.Generic.IEnumerator{System.Activities.ActivityInstance}" />
        /// <remarks>
        /// Does a depth first walk and uses some knowledge of the abort process to determine which child to visit next
        /// </remarks>
        private class AbortEnumerator : IEnumerator<ActivityInstance>
        {
            private readonly ActivityInstance root;
            private bool initialized;

            public AbortEnumerator(ActivityInstance root) => this.root = root;

            public ActivityInstance Current { get; private set; }

            object IEnumerator.Current => this.Current;

            public bool MoveNext()
            {
                if (!this.initialized)
                {
                    this.Current = this.root;

                    // We start by diving down the tree along the
                    // "first child" path
                    while (this.Current.HasChildren)
                    {
                        this.Current = this.Current.GetChildren()[0];
                    }

                    this.initialized = true;

                    return true;
                }
                else
                {
                    if (this.Current == this.root)
                    {
                        // We're done if we returned all the way to the root last time
                        return false;
                    }
                    else
                    {
                        Fx.Assert(
                            !this.Current.Parent.GetChildren().Contains(this.Current),
                            "We should always have removed the current one from the parent's list by now.");

                        this.Current = this.Current.Parent;

                        // Dive down the tree of remaining first children
                        while (this.Current.HasChildren)
                        {
                            this.Current = this.Current.GetChildren()[0];
                        }

                        return true;
                    }
                }
            }

            public void Reset()
            {
                this.Current = null;
                this.initialized = false;
            }

            public void Dispose()
            {
                // no op
            }
        }
    }
}