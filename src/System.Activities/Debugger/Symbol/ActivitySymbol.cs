// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Debugger.Symbol
{
    using System.Globalization;
    using System.IO;

    /// <summary>
    /// Represent the debug symbol for an Activity.
    /// It defines the start/end of Activity in the Xaml file.
    /// </summary>
    public class ActivitySymbol
    {
        /// <summary>
        /// Gets or sets the start line.
        /// </summary>
        public int StartLine { get; internal set; }

        /// <summary>
        /// Gets or sets the start column.
        /// </summary>
        public int StartColumn { get; internal set; }

        /// <summary>
        /// Gets or sets the end line.
        /// </summary>
        public int EndLine { get; internal set; }

        /// <summary>
        /// Gets or sets the end column.
        /// </summary>
        public int EndColumn { get; internal set; }
        
        /// <summary>
        /// Gets or sets the internal representation of qualified identifier.
        /// </summary>
        internal byte[] QualifiedId { get; set; }

        private string id;

        /// <summary>
        /// Gets the publicly available identifier.
        /// </summary>
        public string Id
        {
            get
            {
                if (this.id == null)
                {
                    this.id = this.QualifiedId == null ? string.Empty : new QualifiedId(this.QualifiedId).ToString();
                }

                return this.id;
            }
        }

        /// <summary>
        /// Binary serializer.
        /// </summary>
        /// <param name="writer">The binary writer.</param>
        internal void Write(BinaryWriter writer)
        {
            SymbolHelper.WriteEncodedInt32(writer, this.StartLine);
            SymbolHelper.WriteEncodedInt32(writer, this.StartColumn);
            SymbolHelper.WriteEncodedInt32(writer, this.EndLine);
            SymbolHelper.WriteEncodedInt32(writer, this.EndColumn);
            if (this.QualifiedId != null)
            {
                SymbolHelper.WriteEncodedInt32(writer, this.QualifiedId.Length);
                writer.Write(this.QualifiedId, 0, this.QualifiedId.Length);
            }
            else
            {
                SymbolHelper.WriteEncodedInt32(writer, 0);
            }
        }

        /// <summary>
        /// Binary deserializer.
        /// </summary>
        /// <param name="reader">The binary reader.</param>
        internal ActivitySymbol(BinaryReader reader)
        {
            this.StartLine = SymbolHelper.ReadEncodedInt32(reader);
            this.StartColumn = SymbolHelper.ReadEncodedInt32(reader);
            this.EndLine = SymbolHelper.ReadEncodedInt32(reader);
            this.EndColumn = SymbolHelper.ReadEncodedInt32(reader);
            var qidLength = SymbolHelper.ReadEncodedInt32(reader);
            if (qidLength > 0)
            {
                this.QualifiedId = reader.ReadBytes(qidLength);
            }
        }

        /// <summary>Initializes a new instance of the <see cref="ActivitySymbol"/> class.</summary>
        internal ActivitySymbol()
        {
        }

        /// <summary>Returns a <see cref="string"/> that represents this instance.</summary>
        /// <returns>A <see cref="string"/> that represents this instance.</returns>
        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4}",
            this.Id,
            this.StartLine,
            this.StartColumn,
            this.EndLine, 
            this.EndColumn);
    }
}
