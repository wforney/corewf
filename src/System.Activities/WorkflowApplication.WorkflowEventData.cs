// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    using System;

    public sealed partial class WorkflowApplication
    {
        private class WorkflowEventData
        {
            public WorkflowEventData(WorkflowApplication instance) => this.Instance = instance;

            public WorkflowApplication Instance
            {
                get;
                private set;
            }

            public Func<IAsyncResult, WorkflowApplication, bool, bool> NextCallback
            {
                get;
                set;
            }

            public Exception UnhandledException
            {
                get;
                set;
            }

            public Activity UnhandledExceptionSource
            {
                get;
                set;
            }

            public string UnhandledExceptionSourceInstance
            {
                get;
                set;
            }
        }
    }
}