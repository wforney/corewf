// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System;
    using System.Collections.Generic;
    using System.Text;

    internal class QualifiedId : IEquatable<QualifiedId>
    {
        private byte[] compressedId;

        public QualifiedId(Activity element)
        {
            var bufferSize = 0;

            var ids = new Stack<int>();
            
            var id = element.InternalId;
            bufferSize += GetEncodedSize(id);
            ids.Push(id);

            var space = element.MemberOf;

            while (space != null && space.ParentId != 0)
            {
                bufferSize += GetEncodedSize(space.ParentId);
                ids.Push(space.ParentId);

                space = space.Parent;
            }

            this.compressedId = new byte[bufferSize];

            var offset = 0;
            while (ids.Count > 0)
            {
                offset += Encode(ids.Pop(), this.compressedId, offset);
            }
        }

        public QualifiedId(byte[] bytes)
        {
            this.compressedId = bytes;
        }

        public QualifiedId(int[] idArray)
        {
            var bufferSize = 0;

            for (var i = 0; i < idArray.Length; i++)
            {
                bufferSize += GetEncodedSize(idArray[i]);
            }

            this.compressedId = new byte[bufferSize];

            var offset = 0;
            for (var i = 0; i < idArray.Length; i++)
            {
                offset += Encode(idArray[i], this.compressedId, offset);
            }
        }

        public static bool TryGetElementFromRoot(Activity root, QualifiedId id, out Activity targetElement)
        {
            return TryGetElementFromRoot(root, id.compressedId, out targetElement);
        }

        public static bool TryGetElementFromRoot(Activity root, byte[] idBytes, out Activity targetElement)
        {
            Fx.Assert(root.MemberOf != null, "We need to have our IdSpaces set up for this to work.");

            var currentActivity = root;
            var currentIdSpace = root.MemberOf;

            var offset = 0;
            while (offset < idBytes.Length)
            {
                offset += Decode(idBytes, offset, out var value);

                if (currentIdSpace == null)
                {
                    targetElement = null;
                    return false;
                }

                currentActivity = currentIdSpace[value];

                if (currentActivity == null)
                {
                    targetElement = null;
                    return false;
                }

                currentIdSpace = currentActivity.ParentOf;
            }

            targetElement = currentActivity;
            return true;
        }

        public static QualifiedId Parse(string value)
        {
            if (!TryParse(value, out var result))
            {
                throw FxTrace.Exception.AsError(new FormatException(SR.InvalidActivityIdFormat));
            }

            return result;
        }

        public static bool TryParse(string value, out QualifiedId result)
        {
            Fx.Assert(!string.IsNullOrEmpty(value), "We should have already made sure it isn't null or empty.");

            var idStrings = value.Split('.');
            var ids = new int[idStrings.Length];
            var bufferSize = 0;

            for (var i = 0; i < idStrings.Length; i++)
            {
                // only support non-negative integers as id segments
                if (!int.TryParse(idStrings[i], out var parsedInt) || parsedInt < 0)
                {
                    result = null;
                    return false;
                }

                ids[i] = parsedInt;
                bufferSize += GetEncodedSize(ids[i]);
            }

            var bytes = new byte[bufferSize];
            var offset = 0;

            for (var i = 0; i < ids.Length; i++)
            {
                offset += Encode(ids[i], bytes, offset);
            }

            result = new QualifiedId(bytes);
            return true;
        }

        public static bool Equals(byte[] lhs, byte[] rhs)
        {
            if (lhs.Length == rhs.Length)
            {
                for (var i = 0; i < lhs.Length; i++)
                {
                    if (lhs[i] != rhs[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        public byte[] AsByteArray()
        {
            // Note that we don't do a copy because we assume all users will
            // treat it as immutable.
            return this.compressedId;
        }

        public int[] AsIDArray()
        {
            var tmpList = new List<int>();
            var offset = 0;
            while (offset < this.compressedId.Length)
            {
                offset += Decode(this.compressedId, offset, out var value);

                tmpList.Add(value);
            }
            return tmpList.ToArray();
        }

        public bool Equals(QualifiedId rhs)
        {
            return Equals(this.compressedId, rhs.compressedId);
        }

        public override string ToString()
        {
            var builder = new StringBuilder();

            var needDot = false;
            var offset = 0;
            while (offset < this.compressedId.Length)
            {
                if (needDot)
                {
                    builder.Append('.');   
                }

                offset += Decode(this.compressedId, offset, out var value);

                builder.Append(value);

                needDot = true;
            }

            return builder.ToString();
        }

        // This is the same Encode/Decode logic as the WCF FramingEncoder
        private static int Encode(int value, byte[] bytes, int offset)
        {
            Fx.Assert(value >= 0, "Must be non-negative");

            var count = 1;
            while ((value & 0xFFFFFF80) != 0)
            {
                bytes[offset++] = (byte)((value & 0x7F) | 0x80);
                count++;
                value >>= 7;
            }
            bytes[offset] = (byte)value;
            return count;
        }

        // This is the same Encode/Decode logic as the WCF FramingEncoder
        private static int Decode(byte[] buffer, int offset, out int value)
        {
            var bytesConsumed = 0;
            value = 0;
            
            while (offset < buffer.Length)
            {
                int next = buffer[offset];
                value |= (next & 0x7F) << (bytesConsumed * 7);
                bytesConsumed++;
                if ((next & 0x80) == 0)
                {
                    break;
                }
                offset++;
            }

            return bytesConsumed;
        }

        private static int GetEncodedSize(int value)
        {
            Fx.Assert(value >= 0, "Must be non-negative");

            var count = 1;
            while ((value & 0xFFFFFF80) != 0)
            {
                count++;
                value >>= 7;
            }
            return count;
        }
    }
}


