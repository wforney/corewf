// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Debugger.Symbol
{
    using System;
    using System.Activities.Internals;
    using System.Activities.Runtime;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Text;

    // Represent debug symbol of a workflow tree (similar to pdb file).
    // It contains the absolute path of the xaml file and the location of each activity in the workflow tree.
    // This is used to instrument the workflow without having access to the original xaml file.
    public class WorkflowSymbol
    {
        /// <summary>
        /// Gets or sets the name of the file.
        /// </summary>
        /// <value>The name of the file.</value>
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the symbols.
        /// </summary>
        /// <value>The symbols.</value>
        public ICollection<ActivitySymbol> Symbols { get; set; }

        private byte[] checksum;

        /// <summary>
        /// Gets the checksum.
        /// </summary>
        /// <returns>System.Byte[].</returns>
        public byte[] GetChecksum() => this.checksum == null ? null : (byte[])this.checksum.Clone();

        /// <summary>
        /// The EncodingFormat enumeration.
        /// </summary>
        [Flags]
        internal enum EncodingFormat : byte
        {
            String = 0x76,    // Format as well as cookie. String format is hidden from public.
            Binary = 0x77,
            Checksum = 0x80            
        }

        /// <summary>
        /// The default encoding format
        /// </summary>
        internal const EncodingFormat DefaultEncodingFormat = EncodingFormat.Binary;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowSymbol"/> class.
        /// </summary>
        public WorkflowSymbol()
        {
        }

        // These constructors are private and used by Decode() method.

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowSymbol"/> class.
        /// </summary>
        /// <param name="reader">The reader.</param>
        /// <param name="checksum">The checksum.</param>
        /// <remarks>
        /// Binary deserializer.        
        /// </remarks>
        private WorkflowSymbol(BinaryReader reader, byte[] checksum)
        {
            this.FileName = reader.ReadString();
            var numSymbols = SymbolHelper.ReadEncodedInt32(reader);
            this.Symbols = new List<ActivitySymbol>(numSymbols);
            for (var i = 0; i < numSymbols; ++i)
            {
                this.Symbols.Add(new ActivitySymbol(reader));
            }

            this.checksum = checksum;
        }

        /// <summary>
        /// Decodes the specified symbol string.
        /// </summary>
        /// <param name="symbolString">The symbol string.</param>
        /// <returns>WorkflowSymbol.</returns>
        /// <exception cref="SerializationException"></exception>
        /// <remarks>
        /// Decode from Base64 string.        
        /// </remarks>
        public static WorkflowSymbol Decode(string symbolString)
        {
            using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(symbolString))))
            {
                byte[] checksum = null;
                var format = (EncodingFormat)reader.ReadByte();
                var payloadBytesCount = Convert.FromBase64String(symbolString).Length - sizeof(EncodingFormat);
                if (0 != (format & EncodingFormat.Checksum))
                {
                    var bytesCount = SymbolHelper.ReadEncodedInt32(reader);
                    checksum = reader.ReadBytes(bytesCount);
                    payloadBytesCount -= SymbolHelper.GetEncodedSize(bytesCount);
                    format &= (~EncodingFormat.Checksum);
                }
                switch (format)
                {
                    case EncodingFormat.Binary:
                        return ParseBinary(reader.ReadBytes(payloadBytesCount), checksum); // Compute the 
                    case EncodingFormat.String:
                        return ParseStringRepresentation(reader.ReadString(), checksum);
                }
            }
            throw FxTrace.Exception.AsError(new SerializationException());
        }

        // Serialization

        /// <summary>
        /// Encodes this instance.
        /// </summary>
        /// <returns>System.String.</returns>
        /// <remarks>
        /// Encode to Base64 string        
        /// </remarks>
        public string Encode() => this.Encode(WorkflowSymbol.DefaultEncodingFormat); // default format

        /// <summary>
        /// Encodes the specified encoding format.
        /// </summary>
        /// <param name="encodingFormat">The encoding format.</param>
        /// <returns>System.String.</returns>
        /// <exception cref="SerializationException"></exception>
        internal string Encode(EncodingFormat encodingFormat)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            if (this.checksum != null)
            {
                writer.Write((byte)(encodingFormat | EncodingFormat.Checksum));
                SymbolHelper.WriteEncodedInt32(writer, this.checksum.Length);
                writer.Write(this.checksum);
            }
            else
            {
                writer.Write((byte)encodingFormat);
            }
            switch (encodingFormat)
            {
                case EncodingFormat.Binary:
                    this.Write(writer);
                    break;
                case EncodingFormat.String:
                    writer.Write(this.ToString());
                    break;
                default:
                    throw FxTrace.Exception.AsError(new SerializationException());
            }

            // Need to copy to a buffer to trim excess capacity.
            var buffer = new byte[ms.Length];
            Array.Copy(ms.GetBuffer(), buffer, ms.Length);
            return Convert.ToBase64String(buffer);
        }

        /// <summary>
        /// Parses the binary.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <param name="checksum">The checksum.</param>
        /// <returns>WorkflowSymbol.</returns>
        /// <remarks>
        /// Binary deserializer        
        /// </remarks>
        private static WorkflowSymbol ParseBinary(byte[] bytes, byte[] checksum)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            return new WorkflowSymbol(reader, checksum);
        }

        /// <summary>
        /// Writes the specified writer.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <remarks>
        /// Binary serializer        
        /// </remarks>
        private void Write(BinaryWriter writer)
        {
            writer.Write(this.FileName ?? string.Empty);
            if (this.Symbols != null)
            {
                SymbolHelper.WriteEncodedInt32(writer, this.Symbols.Count);
                foreach (var actSym in this.Symbols)
                {
                    actSym.Write(writer);
                }
            }
            else
            {
                SymbolHelper.WriteEncodedInt32(writer, 0);
            }
        }

        // String encoding serialization.

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        /// <remarks>
        /// This is used for String encoding format.        
        /// </remarks>
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendFormat("{0}", this.FileName ?? string.Empty);
            if (this.Symbols != null)
            {
                foreach (var symbol in this.Symbols)
                {
                    builder.AppendFormat(";{0}", symbol.ToString());
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Parses the string representation.
        /// </summary>
        /// <param name="symbolString">The symbol string.</param>
        /// <param name="checksum">The checksum.</param>
        /// <returns>WorkflowSymbol.</returns>
        /// <remarks>
        /// Deserialization of string encoding format.        
        /// </remarks>
        private static WorkflowSymbol ParseStringRepresentation(string symbolString, byte[] checksum)
        {
            var s = symbolString.Split(';');
            var numSymbols = s.Length - 1;
            var symbols = new ActivitySymbol[numSymbols];
            for (var i = 0; i < numSymbols; ++i)
            {
                var symbolSegments = s[i + 1].Split(',');
                Fx.Assert(symbolSegments.Length == 5, "Invalid activity symbol");
                symbols[i] = new ActivitySymbol
                {
                    QualifiedId = QualifiedId.Parse(symbolSegments[0]).AsByteArray(),
                    StartLine = int.Parse(symbolSegments[1], CultureInfo.InvariantCulture),
                    StartColumn = int.Parse(symbolSegments[2], CultureInfo.InvariantCulture),
                    EndLine = int.Parse(symbolSegments[3], CultureInfo.InvariantCulture),
                    EndColumn = int.Parse(symbolSegments[4], CultureInfo.InvariantCulture)
                };
            }

            return new WorkflowSymbol
            {
                FileName = s[0],
                Symbols = symbols,
                checksum = checksum
            };
        }

        /// <summary>
        /// Calculates the checksum.
        /// </summary>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        public bool CalculateChecksum()
        {
            this.checksum = null;
            if (!string.IsNullOrEmpty(this.FileName))
            {
                this.checksum = SymbolHelper.CalculateChecksum(this.FileName);
            }

            return (this.checksum != null);
        }
    }
}
