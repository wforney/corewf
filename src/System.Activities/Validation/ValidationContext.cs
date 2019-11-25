// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Validation
{
    using System.Activities.Runtime;
    using System.Collections.Generic;

    [Fx.Tag.XamlVisible(false)]
    public sealed class ValidationContext
    {
        private ActivityUtilities.ChildActivity owner;
        private ActivityUtilities.ActivityCallStack parentChain;
        private readonly LocationReferenceEnvironment environment;
        private IList<ValidationError> getChildrenErrors;
        private readonly ProcessActivityTreeOptions options;

        internal ValidationContext(ActivityUtilities.ChildActivity owner, ActivityUtilities.ActivityCallStack parentChain, ProcessActivityTreeOptions options, LocationReferenceEnvironment environment)
        {
            this.owner = owner;
            this.parentChain = parentChain;
            this.options = options;
            this.environment = environment;
        }

        internal LocationReferenceEnvironment Environment
        {
            get { return this.environment; }
        }

        internal IEnumerable<Activity> GetParents()
        {
            var parentsList = new List<Activity>();

            for (var i = 0; i < parentChain.Count; i++)
            {
                parentsList.Add(parentChain[i].Activity);
            }

            return parentsList;
        }

        internal IEnumerable<Activity> GetWorkflowTree()
        {
            // It is okay to just walk the declared parent chain here
            var currentNode = this.owner.Activity;
            if (currentNode != null)
            {
                while (currentNode.Parent != null)
                {
                    currentNode = currentNode.Parent;
                }
                var nodes = ActivityValidationServices.GetChildren(new ActivityUtilities.ChildActivity(currentNode, true), new ActivityUtilities.ActivityCallStack(), this.options);
                nodes.Add(currentNode);
                return nodes;
            }
            else
            {
                return ActivityValidationServices.EmptyChildren;
            }            
        }

        internal IEnumerable<Activity> GetChildren()
        {
            if (!this.owner.Equals(ActivityUtilities.ChildActivity.Empty))
            {
                return ActivityValidationServices.GetChildren(this.owner, this.parentChain, this.options);
            }
            else
            {
                return ActivityValidationServices.EmptyChildren;
            }
        }

        internal void AddGetChildrenErrors(ref IList<ValidationError> validationErrors)
        {
            if (this.getChildrenErrors != null && this.getChildrenErrors.Count > 0)
            {
                if (validationErrors == null)
                {
                    validationErrors = new List<ValidationError>();
                }

                for (var i = 0; i < this.getChildrenErrors.Count; i++)
                {
                    validationErrors.Add(this.getChildrenErrors[i]);
                }

                this.getChildrenErrors = null;
            }
        }
    }
}
