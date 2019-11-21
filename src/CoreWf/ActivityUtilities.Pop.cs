// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System.Activities.Runtime;

    internal static partial class ActivityUtilities
    {
        /// <summary>
        /// The Pop class. Implements the <see cref="System.Activities.Activity" />
        /// </summary>
        /// <seealso cref="System.Activities.Activity" />
        /// <remarks>
        /// We don't implement anything in this class. We just use it as a placeholder for when to
        /// pop off our parent stack.
        /// </remarks>
        private class Pop : Activity
        {
            internal override void InternalExecute(ActivityInstance instance, ActivityExecutor executor, BookmarkManager bookmarkManager) =>
                throw Fx.AssertAndThrow("should never get here");

            internal override void OnInternalCacheMetadata(bool createEmptyBindings) =>
                throw Fx.AssertAndThrow("should never get here");
        }
    }
}
