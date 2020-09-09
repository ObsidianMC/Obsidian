using JetBrains.Annotations;
using System;
using System.Text;

namespace Obsidian.Nbt.Tags
{
    /// <summary> A tag containing an array of signed 64-bit integers. </summary>
    public sealed class NbtLongArray : NbtTag
    {
        private static readonly long[] ZeroArray = new long[0];

        /// <summary> Type of this tag (LongArray). </summary>
        public override NbtTagType TagType
        {
            get { return NbtTagType.LongArray; }
        }

        /// <summary> Value/payload of this tag (an array of signed 64-bit integers). Value is stored as-is and is NOT cloned. May not be <c>null</c>. </summary>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        [NotNull]
        public long[] Value
        {
            get { return longs; }
            set
            {
                longs = value ?? throw new ArgumentNullException("value");
            }
        }

        [NotNull]
        private long[] longs;

        /// <summary> Creates an unnamed NbtLongArray tag, containing an empty array of ints. </summary>
        public NbtLongArray() : this((string)null) { }

        /// <summary> Creates an unnamed NbtIntArray tag, containing the given array of ints. </summary>
        /// <param name="value"> Int array to assign to this tag's Value. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        /// <remarks> Given int array will be cloned. To avoid unnecessary copying, call one of the other constructor
        /// overloads (that do not take a long[]) and then set the Value property yourself. </remarks>
        public NbtLongArray([NotNull] long[] value) : this(null, value) { }

        /// <summary> Creates an NbtLongArray tag with the given name, containing an empty array of ints. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        public NbtLongArray([CanBeNull] string tagName)
        {
            name = tagName;
            longs = ZeroArray;
        }

        /// <summary> Creates an NbtIntArray tag with the given name, containing the given array of ints. </summary>
        /// <param name="tagName"> Name to assign to this tag. May be <c>null</c>. </param>
        /// <param name="value"> long array to assign to this tag's Value. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="value"/> is <c>null</c>. </exception>
        /// <remarks> Given long array will be cloned. To avoid unnecessary copying, call one of the other constructor
        /// overloads (that do not take a int[]) and then set the Value property yourself. </remarks>
        public NbtLongArray([CanBeNull] string tagName, [NotNull] long[] value)
        {
            if (value == null)
                throw new ArgumentNullException("value");

            name = tagName;
            longs = (long[])value.Clone();
        }

        /// <summary> Creates a deep copy of given NbtIntArray. </summary>
        /// <param name="other"> Tag to copy. May not be <c>null</c>. </param>
        /// <exception cref="ArgumentNullException"> <paramref name="other"/> is <c>null</c>. </exception>
        /// <remarks> long array of given tag will be cloned. </remarks>
        public NbtLongArray([NotNull] NbtLongArray other)
        {
            if (other == null)
                throw new ArgumentNullException("other");

            name = other.name;
            longs = (long[])other.Value.Clone();
        }

        /// <summary> Gets or sets an integer at the given index. </summary>
        /// <param name="tagIndex"> The zero-based index of the element to get or set. </param>
        /// <returns> The long at the specified index. </returns>
        /// <exception cref="IndexOutOfRangeException"> <paramref name="tagIndex"/> is outside the array bounds. </exception>
        public new long this[int tagIndex]
        {
            get { return Value[tagIndex]; }
            set { Value[tagIndex] = value; }
        }

        internal override bool ReadTag(NbtBinaryReader readStream)
        {
            int length = readStream.ReadInt32();
            if (length < 0)
            {
                throw new NbtFormatException("Negative length given in TAG_Int_Array");
            }

            if (readStream.Selector != null && !readStream.Selector(this))
            {
                readStream.Skip(length * sizeof(int));
                return false;
            }

            Value = new long[length];
            for (int i = 0; i < length; i++)
            {
                Value[i] = readStream.ReadInt64();
            }
            return true;
        }

        internal override void SkipTag(NbtBinaryReader readStream)
        {
            int length = readStream.ReadInt32();
            if (length < 0)
                throw new NbtFormatException("Negative length given in TAG_LONG_Array");

            readStream.Skip(length * sizeof(int));
        }

        internal override void WriteTag(NbtBinaryWriter writeStream)
        {
            writeStream.Write(NbtTagType.IntArray);
            if (Name == null)
                throw new NbtFormatException("Name is null");

            writeStream.Write(Name);
            WriteData(writeStream);
        }

        internal override void WriteData(NbtBinaryWriter writeStream)
        {
            writeStream.Write(Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                writeStream.Write(Value[i]);
            }
        }

        /// <inheritdoc />
        public override object Clone()
        {
            return new NbtLongArray(this);
        }

        internal override void PrettyPrint(StringBuilder sb, string indentString, int indentLevel)
        {
            for (int i = 0; i < indentLevel; i++)
            {
                sb.Append(indentString);
            }
            sb.Append("TAG_LONG_Array");
            if (!string.IsNullOrEmpty(Name))
            {
                sb.AppendFormat("(\"{0}\")", Name);
            }
            sb.AppendFormat(": [{0} longs]", longs.Length);
        }
    }
}