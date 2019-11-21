// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;

    /// <summary>
    /// The ActivityUtilities class.
    /// </summary>
    internal static partial class ActivityUtilities
    {
        /// <summary>
        /// The ChildActivity structure. Implements the <see
        /// cref="System.IEquatable{System.Activities.ActivityUtilities.ChildActivity}" />
        /// </summary>
        /// <seealso cref="System.IEquatable{System.Activities.ActivityUtilities.ChildActivity}" />
        public struct ChildActivity : IEquatable<ChildActivity>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ChildActivity" /> struct.
            /// </summary>
            /// <param name="activity">The activity.</param>
            /// <param name="canBeExecuted">if set to <c>true</c> [can be executed].</param>
            public ChildActivity(Activity activity, bool canBeExecuted)
                : this()
            {
                this.Activity = activity;
                this.CanBeExecuted = canBeExecuted;
            }

            /// <summary>
            /// Gets the empty.
            /// </summary>
            /// <value>The empty.</value>
            public static ChildActivity Empty => new ChildActivity();

            /// <summary>
            /// Gets or sets the activity.
            /// </summary>
            /// <value>The activity.</value>
            public Activity Activity
            {
                get;
                set;
            }

            /// <summary>
            /// Gets or sets a value indicating whether this instance can be executed.
            /// </summary>
            /// <value><c>true</c> if this instance can be executed; otherwise, <c>false</c>.</value>
            public bool CanBeExecuted
            {
                get;
                set;
            }

            /// <summary>
            /// Implements the != operator.
            /// </summary>
            /// <param name="left">The left.</param>
            /// <param name="right">The right.</param>
            /// <returns>The result of the operator.</returns>
            public static bool operator !=(ChildActivity left, ChildActivity right) => !(left == right);

            /// <summary>
            /// Implements the == operator.
            /// </summary>
            /// <param name="left">The left.</param>
            /// <param name="right">The right.</param>
            /// <returns>The result of the operator.</returns>
            public static bool operator ==(ChildActivity left, ChildActivity right) => left.Equals(right);

            /// <summary>
            /// Indicates whether the current object is equal to another object of the same type.
            /// </summary>
            /// <param name="other">An object to compare with this object.</param>
            /// <returns>
            /// <see langword="true" /> if the current object is equal to the <paramref name="other"
            /// /> parameter; otherwise, <see langword="false" />.
            /// </returns>
            public bool Equals(ChildActivity other) => object.ReferenceEquals(this.Activity, other.Activity) && this.CanBeExecuted == other.CanBeExecuted;

            /// <summary>
            /// Determines whether the specified <see cref="System.Object" /> is equal to this instance.
            /// </summary>
            /// <param name="obj">The object to compare with the current instance.</param>
            /// <returns>
            /// <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance;
            /// otherwise, <c>false</c>.
            /// </returns>
            public override bool Equals(object obj) => obj is ChildActivity && this.Equals((ChildActivity)obj);

            /// <summary>
            /// Returns a hash code for this instance.
            /// </summary>
            /// <returns>
            /// A hash code for this instance, suitable for use in hashing algorithms and data
            /// structures like a hash table.
            /// </returns>
            public override int GetHashCode() => HashCode.Combine(this.Activity, this.CanBeExecuted);
        }
    }
}
