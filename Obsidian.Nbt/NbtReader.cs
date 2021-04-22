using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Obsidian.Nbt
{
    public class NbtReader : BinaryReader
    {
        public NbtTagType? CurrentTag { get; set; }

        public NbtReader() : this(new MemoryStream()) { }

        public NbtReader(Stream input) : base(input) { }

        public NbtTagType ReadTagType()
        {
            var type = this.ReadByte();

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
                    return new NbtTag(type, this.ReadString(), this.ReadByte());
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

        public NbtTag ReadNextTag()
        {
            var firstType = this.ReadTagType();

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
                case NbtTagType.ByteArray:
                    break;
                case NbtTagType.String:
                    break;
                case NbtTagType.List:
                    break;
                case NbtTagType.Compound:
                    {
                        var compoundName = this.ReadString();
                        var compound = new NbtCompound(compoundName);

                        NbtTagType type;
                        while ((type = this.ReadTagType()) != NbtTagType.End)
                        {
                            var tag = this.GetCurrentTag(type);

                            compound.AddTag(tag.Name, tag);
                        }

                        return compound;
                    }
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

        public override string ReadString()
        {
            var length = this.ReadInt16();

            if (length < 0)
                throw new InvalidOperationException("Negative length value found");

            var data = this.ReadBytes(length);

            return Encoding.UTF8.GetString(data);
        }

        public override short ReadInt16()
        {
            Span<byte> buffer = stackalloc byte[2];
            this.Read(buffer);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            return BitConverter.ToInt16(buffer);
        }

        public override int ReadInt32()
        {
            Span<byte> buffer = stackalloc byte[4];
            this.Read(buffer);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            return BitConverter.ToInt32(buffer);
        }

        public override long ReadInt64()
        {
            Span<byte> buffer = stackalloc byte[8];
            this.Read(buffer);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            return BitConverter.ToInt64(buffer);
        }

        public override float ReadSingle()
        {
            Span<byte> buffer = stackalloc byte[4];
            this.Read(buffer);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();


            return BitConverter.ToSingle(buffer);
        }

        public override double ReadDouble()
        {
            Span<byte> buffer = stackalloc byte[4];
            this.Read(buffer);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            return BitConverter.ToDouble(buffer);
        }
        #endregion
    }
}
