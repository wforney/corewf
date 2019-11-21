// This file is part of Core WF which is licensed under the MIT license. See LICENSE file in the
// project root for full license information.

namespace System.Activities
{
    internal static partial class ActivityUtilities
    {
        public class ActivityCallStack
        {
            private readonly Quack<ChildActivity> callStack;
            private int nonExecutingParentCount;

            public ActivityCallStack() => this.callStack = new Quack<ChildActivity>();

            public int Count => this.callStack.Count;

            public bool WillExecute => this.nonExecutingParentCount == 0;

            public ChildActivity this[int index] => this.callStack[index];

            public ChildActivity Pop()
            {
                var childActivity = this.callStack.Dequeue();

                if (!childActivity.CanBeExecuted)
                {
                    this.nonExecutingParentCount--;
                }

                return childActivity;
            }

            public void Push(ChildActivity childActivity)
            {
                if (!childActivity.CanBeExecuted)
                {
                    this.nonExecutingParentCount++;
                }

                this.callStack.PushFront(childActivity);
            }
        }
    }
}
