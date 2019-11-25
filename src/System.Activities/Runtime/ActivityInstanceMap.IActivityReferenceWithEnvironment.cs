// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    using System.Activities.DynamicUpdate;

    internal partial class ActivityInstanceMap
    {
        public interface IActivityReferenceWithEnvironment : IActivityReference
        {
            void UpdateEnvironment(EnvironmentUpdateMap map, Activity activity);
        }
    }
}
