// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    using System;

    public abstract partial class Activity
    {

internal partial class ReflectedInformation
        {
            /// <summary>
            /// The ReflectedType enumeration.
            /// </summary>
            [Flags]
            private enum ReflectedType
            {
                /// <summary>
                /// The argument
                /// </summary>
                Argument = 0X1,

                /// <summary>
                /// The variable
                /// </summary>
                Variable = 0X2,

                /// <summary>
                /// The child
                /// </summary>
                Child = 0X4,

                /// <summary>
                /// The activity delegate
                /// </summary>
                ActivityDelegate = 0X8,

                /// <summary>
                /// All
                /// </summary>
                All = 0XF
            }
        }
    }
}