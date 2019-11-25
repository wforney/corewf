// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Debugger
{
    using System.Activities.Hosting;
    using System.Activities.Validation;
    using System.Collections.Generic;
    using System.Diagnostics;

    // DebugController, one is needed per ActivityExecutor.
    [DebuggerNonUserCode]
    internal class DebugController
    {
        private DebugManager debugManager;
        private WorkflowInstance host;
        // Instantiated after first instrumentation is successful.

        public DebugController(WorkflowInstance host)
        {
            this.host = host;
        }

        public void ActivityCompleted(ActivityInstance activityInstance)
        {
            if (!(activityInstance.Activity.RootActivity is Constraint)) // Don't debug an activity in a Constraint
            {
                EnsureActivityInstrumented(activityInstance, true);
                this.debugManager.OnLeaveState(activityInstance);
            }
        }

        public void ActivityStarted(ActivityInstance activityInstance)
        {
            if (!(activityInstance.Activity.RootActivity is Constraint))  // Don't debug an activity in a Constraint
            {
                EnsureActivityInstrumented(activityInstance, false);
                this.debugManager.OnEnterState(activityInstance);
            }
        }

        public void WorkflowCompleted()
        {
            if (this.debugManager != null)
            {
                this.debugManager.Exit();
                this.debugManager = null;
            }
        }

        public void WorkflowStarted()
        {
        }

        // Lazy instrumentation. Parameter primeCurrentInstance specify whether priming (if needed)
        // is done up to the current instance. Set this to true when calling this from an
        // "...Completed" (exit state).
        private void EnsureActivityInstrumented(ActivityInstance instance, bool primeCurrentInstance)
        {
            if (this.debugManager == null)
            {   // Workflow has not been instrumented yet.
                // Finding rootInstance and check all referred sources.
                Stack<ActivityInstance> ancestors = new Stack<ActivityInstance>();
                while (instance.Parent != null)
                {
                    ancestors.Push(instance);
                    instance = instance.Parent;
                }

                Activity rootActivity = instance.Activity;

                // Do breakOnStartup only if debugger is attached from the beginning, i.e. no
                // priming needed. This specified by change the last parameter below to:
                // "(ancestors.Count == 0)".
                this.debugManager = new DebugManager(rootActivity, "Workflow", "Workflow", "DebuggerThread", false, this.host, ancestors.Count == 0);

                if (ancestors.Count > 0)
                {
                    // Priming the background thread
                    this.debugManager.IsPriming = true;
                    while (ancestors.Count > 0)
                    {
                        ActivityInstance ancestorInstance = ancestors.Pop();
                        this.debugManager.OnEnterState(ancestorInstance);
                    }
                    if (primeCurrentInstance)
                    {
                        this.debugManager.OnEnterState(instance);
                    }
                    this.debugManager.IsPriming = false;
                }
            }
        }
    }
}
