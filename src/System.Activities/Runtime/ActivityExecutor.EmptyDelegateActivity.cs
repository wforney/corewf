// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    internal partial class ActivityExecutor
    {
        /// <summary>
        /// This is used in ScheduleDelegate when the handler is null. We use this dummy activity to
        /// set as the 'Activity' of the completed ActivityInstance.
        /// </summary>
        private class EmptyDelegateActivity : NativeActivity
        {
            internal EmptyDelegateActivity()
            {
            }

            protected override void Execute(NativeActivityContext context) => 
                Fx.Assert(false, Properties.Resources.ThisActivityShouldNeverBeExecutedItIsADummyActivity);
        }
    }
}
