// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System;
    using System.Collections.Generic;

    public sealed partial class RuntimeArgument
    {
        /// <summary>
        /// The InternalEvaluationOrderComparer class.
        /// Implements the <see cref="System.Collections.Generic.IComparer{System.Activities.RuntimeArgument}" />
        /// </summary>
        /// <seealso cref="System.Collections.Generic.IComparer{System.Activities.RuntimeArgument}" />
        private class InternalEvaluationOrderComparer : IComparer<RuntimeArgument>
        {
            /// <summary>
            /// Compares two objects and returns a value indicating whether one is less than, equal to, or greater than the other.
            /// </summary>
            /// <param name="x">The first object to compare.</param>
            /// <param name="y">The second object to compare.</param>
            /// <returns>A signed integer that indicates the relative values of <paramref name="x" /> and <paramref name="y" />, as shown in the following table.
            /// Value
            /// Meaning
            /// Less than zero
            /// <paramref name="x" /> is less than <paramref name="y" />.
            /// Zero
            /// <paramref name="x" /> equals <paramref name="y" />.
            /// Greater than zero
            /// <paramref name="x" /> is greater than <paramref name="y" />.</returns>
            public int Compare(RuntimeArgument x, RuntimeArgument y) =>
                x.IsEvaluationOrderSpecified
                    ? y.IsEvaluationOrderSpecified
                        ? x.BoundArgument?.EvaluationOrder.CompareTo(y.BoundArgument?.EvaluationOrder) ?? 0
                        : 1
                    : y.IsEvaluationOrderSpecified ? -1 : this.CompareNameHashes(x, y);

            /// <summary>
            /// Compares the name hashes.
            /// </summary>
            /// <param name="x">The x.</param>
            /// <param name="y">The y.</param>
            /// <returns>System.Int32.</returns>
            private int CompareNameHashes(RuntimeArgument x, RuntimeArgument y)
            {
                x.EnsureHash();
                y.EnsureHash();

                return x.nameHash == y.nameHash 
                    ? string.Compare(x.Name, y.Name, StringComparison.CurrentCulture)
                    : x.nameHash.CompareTo(y.nameHash);
            }
        }
    }
}
