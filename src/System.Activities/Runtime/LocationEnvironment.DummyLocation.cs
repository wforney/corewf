// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    internal sealed partial class LocationEnvironment
    {
        private class DummyLocation : Location<object>
        {
            // this is a dummy location temporarary place holder for a dynamically added LocationReference
        }
    }
}
