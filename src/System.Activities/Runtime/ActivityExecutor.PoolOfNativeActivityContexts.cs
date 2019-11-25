// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Runtime
{
    internal partial class ActivityExecutor
    {
        private class PoolOfNativeActivityContexts : Pool<NativeActivityContext>
        {
            protected override NativeActivityContext CreateNew() => new NativeActivityContext();
        }
    }
}
