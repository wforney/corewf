// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System.Activities.Runtime;
    using System.Security;

    public sealed partial class RuntimeArgument
    {
        /// <summary>
        /// This class implements iSCSI CRC-32 check outlined in IETF RFC 3720.
        /// it's marked internal so that DataModel CIT can access it        
        /// </summary>
        internal static class CRCHashCode
        {
            /// <summary>
            /// Reflected value for iSCSI CRC-32 polynomial 0x1edc6f41            
            /// </summary>
            private const uint polynomial = 0x82f63b78;

            [Fx.Tag.SecurityNote(Critical = "Critical because it is marked unsafe.",
                Safe = "Safe because we aren't leaking anything. We are just using pointers to get into the string.")]
            [SecuritySafeCritical]
            public static unsafe uint Calculate(string s)
            {
                var result = 0xffffffff;
                var byteLength = s.Length * sizeof(char);

                fixed (char* pString = s)
                {
                    var pbString = (byte*)pString;
                    for (var i = 0; i < byteLength; i++)
                    {
                        result ^= pbString[i];
                        result = ((result & 1) * polynomial) ^ (result >> 1);
                        result = ((result & 1) * polynomial) ^ (result >> 1);
                        result = ((result & 1) * polynomial) ^ (result >> 1);
                        result = ((result & 1) * polynomial) ^ (result >> 1);
                        result = ((result & 1) * polynomial) ^ (result >> 1);
                        result = ((result & 1) * polynomial) ^ (result >> 1);
                        result = ((result & 1) * polynomial) ^ (result >> 1);
                        result = ((result & 1) * polynomial) ^ (result >> 1);
                    }
                }

                return ~result;
            }
        }
    }
}
