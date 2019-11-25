// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities
{
    public abstract partial class Activity
    {
        /// <summary>
        /// The RelationshipType enumeration.
        /// </summary>
        internal enum RelationshipType : byte
        {
            /// <summary>
            /// The child
            /// </summary>
            Child = 0x00,

            /// <summary>
            /// The imported child
            /// </summary>
            ImportedChild = 0x01,

            /// <summary>
            /// The implementation child
            /// </summary>
            ImplementationChild = 0x02,

            /// <summary>
            /// The delegate handler
            /// </summary>
            DelegateHandler = 0x03,

            /// <summary>
            /// The argument expression
            /// </summary>
            ArgumentExpression = 0x04,

            /// <summary>
            /// The variable default
            /// </summary>
            VariableDefault = 0x05
        }
    }
}