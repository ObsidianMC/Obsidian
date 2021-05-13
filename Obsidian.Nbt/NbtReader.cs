using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Obsidian.Nbt
{
    public class NbtReader
    {
        public NbtTagType? CurrentTag { get; set; }

        public Stream BaseStream { get; }

        public NbtReader() : this(new MemoryStream()) { }

        public NbtReader(Stream input, bool gzip = false)
        {
            if(gzip)
            {
                this.BaseStream = new GZipStream(input, CompressionMode.Decompress);

                return;
            }

            this.BaseStream = input;
        }

        public NbtTagType ReadTagType()
        {
            var type = this.BaseStream.ReadByte();

            if (type < 0)
                return NbtTagType.End;
            else if (type > (byte)NbtTagType.LongArray)
                throw new ArgumentOutOfRangeException($"Tag is out of range: {(NbtTagType)type}");

            return (NbtTagType)type;
        }

        private NbtTag GetCurrentTag(NbtTagType type)
        {
            switch (type)
            {
                case NbtTagType.End:
                    return new NbtTag(type);
                case NbtTagType.Byte:
                    return new NbtTag(type, this.ReadString(), this.BaseStream.ReadByte());
                case NbtTagType.Short:
                    return new NbtTag(type, this.ReadString(), this.ReadInt16());
                case NbtTagType.Int:
                    return new NbtTag(type, this.ReadString(), this.ReadInt32());
                case NbtTagType.Long:
                    return new NbtTag(type, this.ReadString(), this.ReadInt64());
                case NbtTagType.Float:
                    return new NbtTag(type, this.ReadString(), this.ReadSingle());
                case NbtTagType.Double:
                    return new NbtTag(type, this.ReadString(), this.ReadDouble());
                case NbtTagType.String:
                    return new NbtTag(type, this.ReadString(), this.ReadString());
                case NbtTagType.List:
                    break;
                case NbtTagType.Compound:
                    break;
                case NbtTagType.IntArray:
                case NbtTagType.LongArray:
                case NbtTagType.ByteArray:
                    var arrayType = this.ReadTagType();
                    var length = this.ReadInt32();


                    break;
                case NbtTagType.Unknown:
                default:
                    break;
            }

            return null;
        }

        public NbtTag ReadNextTag(bool readName = true)
        {
            var firstType = this.ReadTagType();

            string tagName = "";

            if (readName)
                tagName = this.ReadString();

            switch (firstType)
            {
                case NbtTagType.End:
                    throw new EndOfStreamException("End of tag has been reached.");
                case NbtTagType.Byte:
                    break;
                case NbtTagType.Short:
                    break;
                case NbtTagType.Int:
                    break;
                case NbtTagType.Long:
                    break;
                case NbtTagType.Float:
                    break;
                case NbtTagType.Double:
                    break;
                case NbtTagType.String:
                    break;
                case NbtTagType.List:
                    var listType = this.ReadTagType();

                    var list = new NbtList(listType, tagName);

                    var length = this.ReadInt32();

                    if (length < 0)
                        throw new InvalidOperationException("Got negative list length.");

                    for (var i = 0; i < length; i++)
                        list.Add(this.ReadNextTag(false));

                    break;
                case NbtTagType.Compound:
                    {
                        var compound = new NbtCompound(tagName);

                        NbtTagType type;
                        while ((type = this.ReadTagType()) != NbtTagType.End)
                        {
                            var tag = this.GetCurrentTag(type);

                            compound.Add(tag);
                        }

                        return compound;
                    }
                case NbtTagType.ByteArray:
                    break;
                case NbtTagType.IntArray:
                    break;
                case NbtTagType.LongArray:
                    break;
                case NbtTagType.Unknown:
                    break;
                default:
                    break;
            }

            return null;
        }

        #region overrides

        public string ReadString()
        {
            var length = this.ReadInt16();

            if (length < 0)
                throw new InvalidOperationException("Negative length value found");

            Span<byte> buffer = stackalloc byte[length];

            this.BaseStream.Read(buffer);

            return Encoding.UTF8.GetString(buffer);
        }

        public short ReadInt16()
        {
            Span<byte> buffer = stackalloc byte[2];
            this.BaseStream.Read(buffer);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            return BitConverter.ToInt16(buffer);
        }

        public int ReadInt32()
        {
            Span<byte> buffer = stackalloc byte[4];
            this.BaseStream.Read(buffer);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            return BitConverter.ToInt32(buffer);
        }

        public long ReadInt64()
        {
            Span<byte> buffer = stackalloc byte[8];
            this.BaseStream.Read(buffer);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            return BitConverter.ToInt64(buffer);
        }

        public float ReadSingle()
        {
            Span<byte> buffer = stackalloc byte[4];
            this.BaseStream.Read(buffer);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();


            return BitConverter.ToSingle(buffer);
        }

        public double ReadDouble()
        {
            Span<byte> buffer = stackalloc byte[4];
            this.BaseStream.Read(buffer);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            return BitConverter.ToDouble(buffer);
        }
        #endregion
    }
}
