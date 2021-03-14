using Obsidian.Nbt.Tags;
using System;
using System.Collections.ObjectModel;

namespace Obsidian.Utilities.Collection
{
    /// <summary>
    /// Represents an array of 4-bit values.
    /// </summary>
    public class NibbleArray //: INbtSerializable
    {
        /// <summary>
        /// The data in the nibble array. Each byte contains
        /// two nibbles, stored in big-endian.
        /// </summary>
        public byte[] Data { get; set; }

        public NibbleArray()
        {
        }

        /// <summary>
        /// Creates a new nibble array with the given number of nibbles.
        /// </summary>
        public NibbleArray(int length)
        {
            this.Data = new byte[length / 2];
        }

        /// <summary>
        /// Gets the current number of nibbles in this array.
        /// </summary>
        //[NbtIgnore]
        public int Length
        {
            get { return this.Data.Length * 2; }
        }

        /// <summary>
        /// Gets or sets a nibble at the given index.
        /// </summary>
        //[NbtIgnore]
        public byte this[int index]
        {
            get => (byte)((this.Data[index / 2] >> (index % 2 * 4)) & 0xF);
            set
            {
                value &= 0xF;
                this.Data[index / 2] &= (byte)(0xF << ((index + 1) % 2 * 4));
                this.Data[index / 2] |= (byte)(value << (index % 2 * 4));
            }
        }

        public NbtTag Serialize(string tagName)
        {
            return new NbtByteArray(tagName, Data);
        }

        public void Deserialize(NbtTag value)
        {
            this.Data = value.ByteArrayValue;
        }
    }

    public class ReadOnlyNibbleArray
    {
        private NibbleArray NibbleArray { get; set; }

        public ReadOnlyNibbleArray(NibbleArray array)
        {
            this.NibbleArray = array;
        }

        public byte this[int index] => this.NibbleArray[index];

        public ReadOnlyCollection<byte> Data
        {
            get { return Array.AsReadOnly(this.NibbleArray.Data); }
        }
    }
}