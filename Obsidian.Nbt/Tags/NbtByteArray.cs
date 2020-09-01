using JetBrains.Annotations;
using System;
using System.IO;
using System.Text;

namespace Obsidian.Nbt.Tags
{
    /// <summary> A tag containing an array of bytes. </summary>
    public sealed class NbtByteArray : NbtTag
    {
        private static readonly byte[] ZeroArray = new byte[0];

        /// <summary> Type of this tag (ByteArray). </summary>
        public override NbtTagType TagType { get; } = NbtTagType.ByteArray;

        /// <summary> Value/payload of this tag (an array of bytes). Value is stored as-is and is NOT cloned. May not be <c>null</c>. </summary>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        [NotNull]
        public byte[] Value
        {
            get => this.bytes;
            set => this.bytes = value ?? throw new ArgumentNullException("value");
        }

        [NotNull]
        private byte[] bytes;

        /// <summary> Creates an unnamed NbtByte tag, containing an empty array of bytes. </summary>
        public NbtByteArray() : this((string)null) { }

        /// <summary> Creates an unnamed NbtByte tag, containing the given array of bytes. </summary>
        /// <param name="value"> Byte array to assign to this tag's Value. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        /// <remarks> Given byte array will be cloned. To avoid unnecessary copying, call one of the other constructor
        /// overloads (that do not take a byte[]) and then set the Value property yourself. </remarks>
        public NbtByteArray([NotNull] byte[] value) : this(null, value) { }

        /// <summary> Creates an NbtByte tag with the given name, containing an empty array of bytes. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        public NbtByteArray([CanBeNull] string tagName)
        {
            this.name = tagName;
            this.bytes = ZeroArray;
        }

        /// <summary> Creates an NbtByte tag with the given name, containing the given array of bytes. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        /// <param name="value"> Byte array to assign to this tag's Value. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        /// <remarks> Given byte array will be cloned. To avoid unnecessary copying, call one of the other constructor
        /// overloads (that do not take a byte[]) and then set the Value property yourself. </remarks>
        public NbtByteArray([CanBeNull] string tagName, [NotNull] byte[] value)
        {
            if (value == null)
                throw new ArgumentNullException("value");

            this.name = tagName;
            this.bytes = (byte[])value.Clone();
        }

        /// <summary> Creates a deep copy of given NbtByteArray. </summary>
        /// <param name="other"> Tag to copy. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="other"/> is <c>null</c>. </exception>
        /// <remarks> Byte array of given tag will be cloned. </remarks>
        public NbtByteArray([NotNull] NbtByteArray other)
        {
            if (other == null)
                throw new ArgumentNullException("other");

            this.name = other.name;
            this.bytes = (byte[])other.Value.Clone();
        }

        /// <summary> Gets or sets a byte at the given index. </summary>
        /// <param name="tagIndex"> The zero-based index of the element to get or set. </param>
        /// <returns> The byte at the specified index. </returns>
        /// <exception cref="IndexOutOfRangeException"> <paramref name="tagIndex"/> is outside the array bounds. </exception>
        public new byte this[int tagIndex]
        {
            get => Value[tagIndex];
            set => this.Value[tagIndex] = value;
        }

        internal override bool ReadTag(NbtBinaryReader readStream)
        {
            int length = readStream.ReadInt32();
            if (length < 0)
                throw new NbtFormatException("Negative length given in TAG_Byte_Array");


            if (readStream.Selector != null && !readStream.Selector(this))
            {
                readStream.Skip(length);
                return false;
            }

            this.Value = readStream.ReadBytes(length);

            if (Value.Length < length)
                throw new EndOfStreamException();

            return true;
        }

        internal override void SkipTag(NbtBinaryReader readStream)
        {
            int length = readStream.ReadInt32();
            if (length < 0)
                throw new NbtFormatException("Negative length given in TAG_Byte_Array");

            readStream.Skip(length);
        }

        internal override void WriteTag(NbtBinaryWriter writeStream)
        {
            writeStream.Write(NbtTagType.ByteArray);
            if (this.Name == null)
                throw new NbtFormatException("Name is null");

            writeStream.Write(this.Name);
            this.WriteData(writeStream);
        }

        internal override void WriteData(NbtBinaryWriter writeStream)
        {
            writeStream.Write(this.Value.Length);
            writeStream.Write(this.Value, 0, this.Value.Length);
        }

        /// <inheritdoc />
        public override object Clone() => new NbtByteArray(this);

        internal override void PrettyPrint(StringBuilder sb, string indentString, int indentLevel)
        {
            for (int i = 0; i < indentLevel; i++)
                sb.Append(indentString);

            sb.Append("TAG_Byte_Array");

            if (!string.IsNullOrEmpty(this.Name))
                sb.AppendFormat("(\"{0}\")", this.Name);

            sb.AppendFormat(": [{0} bytes]", this.bytes.Length);
        }
    }
}