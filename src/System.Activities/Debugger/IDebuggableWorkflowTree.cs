// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities.Debugger
{
    /// <summary>
    /// The IDebuggableWorkflowTree interface.
    /// </summary>
    /// <remarks>
    /// Interface to implement in serializable object containing Workflow to be debuggable with
    /// Workflow debugger.
    /// </remarks>
    public interface IDebuggableWorkflowTree
    {
        /// <summary>
        /// Gets the workflow root.
        /// </summary>
        /// <returns>Return the root of the workflow tree.</returns>
        Activity? GetWorkflowRoot();
    }
}
