// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Debugger
{
    using System.Collections.Generic;
    using System.Activities.Debugger.Symbol;

    // Keep track of instrumentation information.
    // - which subroot has source file but not yet instrumented.
    // - which subroots share the same source file
    // SubRoot is defined as an activity that has a source file
    // (Custom Activity).
    internal class InstrumentationTracker
    {
        // Root of the workflow to keep track.
        private Activity root;

        // Mapping of subroots to their source files.
        private Dictionary<Activity, string> uninstrumentedSubRoots;

        private Dictionary<Activity, string> UninstrumentedSubRoots
        {
            get
            {
                if (this.uninstrumentedSubRoots == null)
                {
                    InitializeUninstrumentedSubRoots();
                }
                return this.uninstrumentedSubRoots;
            }
        }

        public InstrumentationTracker(Activity root)
        {
            this.root = root;
        }

        // Initialize UninstrumentedSubRoots by traversing the workflow.
        private void InitializeUninstrumentedSubRoots()
        {
            this.uninstrumentedSubRoots = new Dictionary<Activity, string>();

            var activitiesRemaining = new Queue<Activity>();

            CollectSubRoot(this.root);
            activitiesRemaining.Enqueue(this.root);

            while (activitiesRemaining.Count > 0)
            {
                var toProcess = activitiesRemaining.Dequeue();

                foreach (var activity in WorkflowInspectionServices.GetActivities(toProcess))
                {
                    if (!uninstrumentedSubRoots.ContainsKey(activity))
                    {
                        CollectSubRoot(activity);
                        activitiesRemaining.Enqueue(activity);
                    }
                }
            }
        }

        // Collect subroot as uninstrumented activity.
        private void CollectSubRoot(Activity activity)
        {
            var wfSymbol = DebugSymbol.GetSymbol(activity) as string;
            if (!string.IsNullOrEmpty(wfSymbol))
            {
                this.uninstrumentedSubRoots.Add(activity, wfSymbol);
            }
            else
            {
                var sourcePath = XamlDebuggerXmlReader.GetFileName(activity) as string;
                if (!string.IsNullOrEmpty(sourcePath))
                {
                    this.uninstrumentedSubRoots.Add(activity, sourcePath);
                }
            }
        }

        // Whether this is unistrumented sub root.
        public bool IsUninstrumentedSubRoot(Activity subRoot)
        {
            return this.UninstrumentedSubRoots.ContainsKey(subRoot);
        }


        // Returns Activities that have the same source as the given subRoot.
        // This will return other instantiation of the same custom activity.
        // Needed to avoid re-instrumentation of the same file.
        public List<Activity> GetSameSourceSubRoots(Activity subRoot)
        {
            string sourcePath;
            var sameSourceSubRoots = new List<Activity>();
            if (this.UninstrumentedSubRoots.TryGetValue(subRoot, out sourcePath))
            {
                foreach (var entry in this.UninstrumentedSubRoots)
                {
                    if (entry.Value == sourcePath && entry.Key != subRoot)
                    {
                        sameSourceSubRoots.Add(entry.Key);
                    }
                }
            }
            return sameSourceSubRoots;
        }

        // Mark this sub root as instrumented.
        public void MarkInstrumented(Activity subRoot)
        {
            this.UninstrumentedSubRoots.Remove(subRoot);
        }
    }
}
