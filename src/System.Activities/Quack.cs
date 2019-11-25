// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Activities.Runtime;
    using System;

    // A mostly output-restricted double-ended queue. You can add an item to both ends
    // and it is optimized for removing from the front.  The list can be scanned and
    // items can be removed from any location at the cost of performance.
    internal class Quack<T>
    {
        private T[] items;

        // First element when items is not empty
        private int head;

        // Next vacancy when items are not full
        private int tail;

        // Number of elements.
        private int count;

        public Quack()
        {
            this.items = new T[4];
        }

        public Quack(T[] items)
        {
            Fx.Assert(items != null, "This shouldn't get called with null");
            Fx.Assert(items.Length > 0, "This shouldn't be called with a zero length array.");

            this.items = items;

            // The default value of 0 is correct for both
            // head and tail.

            this.count = this.items.Length;
        }

        public int Count
        {
            get { return this.count; }
        }

        public T this[int index]
        {
            get
            {
                Fx.Assert(index < this.count, "Index out of range.");

                var realIndex = (this.head + index) % this.items.Length;

                return this.items[realIndex];
            }
        }

        public T[] ToArray()
        {
            Fx.Assert(this.count > 0, "We should only call this when we have items.");

            var compressedItems = new T[this.count];

            for (var i = 0; i < this.count; i++)
            {
                compressedItems[i] = this.items[(this.head + i) % this.items.Length];
            }

            return compressedItems;
        }

        public void PushFront(T item)
        {
            if (this.count == this.items.Length)
            {
                Enlarge();
            }

            if (--this.head == -1)
            {
                this.head = this.items.Length - 1;
            }
            this.items[this.head] = item;

            ++this.count;
        }

        public void Enqueue(T item)
        {
            if (this.count == this.items.Length)
            {
                Enlarge();
            }

            this.items[this.tail] = item;
            if (++this.tail == this.items.Length)
            {
                this.tail = 0;
            }

            ++this.count;
        }

        public T Dequeue()
        {
            Fx.Assert(this.count > 0, "Quack is empty");

            var removed = this.items[this.head];
            this.items[this.head] = default(T);
            if (++this.head == this.items.Length)
            {
                this.head = 0;
            }

            --this.count;

            return removed;
        }

        public bool Remove(T item)
        {
            var found = -1;

            for (var i = 0; i < this.count; i++)
            {
                var realIndex = (this.head + i) % this.items.Length;
                if (object.Equals(this.items[realIndex], item))
                {
                    found = i;
                    break;
                }
            }

            if (found == -1)
            {
                return false;
            }
            else
            {
                Remove(found);
                return true;
            }
        }

        public void Remove(int index)
        {
            Fx.Assert(index < this.count, "Index out of range");

            for (var i = index - 1; i >= 0; i--)
            {
                var sourceIndex = (this.head + i) % this.items.Length;
                var targetIndex = sourceIndex + 1;

                if (targetIndex == this.items.Length)
                {
                    targetIndex = 0;
                }

                this.items[targetIndex] = this.items[sourceIndex];
            }

            --this.count;
            ++this.head;

            if (this.head == this.items.Length)
            {
                this.head = 0;
            }
        }

        private void Enlarge()
        {
            Fx.Assert(this.items.Length > 0, "Quack is empty");

            var capacity = this.items.Length * 2;
            this.SetCapacity(capacity);
        }

        private void SetCapacity(int capacity)
        {
            Fx.Assert(capacity >= this.count, "Capacity is set to a smaller value");

            var newArray = new T[capacity];
            if (this.count > 0)
            {
                if (this.head < this.tail)
                {
                    Array.Copy(this.items, this.head, newArray, 0, this.count);
                }
                else
                {
                    Array.Copy(this.items, this.head, newArray, 0, this.items.Length - this.head);
                    Array.Copy(this.items, 0, newArray, this.items.Length - this.head, this.tail);
                }
            }

            this.items = newArray;
            this.head = 0;
            this.tail = (this.count == capacity) ? 0 : this.count;
        }
    }
}
