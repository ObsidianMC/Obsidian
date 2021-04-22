using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Obsidian.Nbt
{
    //TODO: 
    public sealed class NbtWriter : BinaryWriter
    {
        private NbtTagType parentType;
        private NbtTagType? listType;

        public bool IsClosed { get; internal set; }

        public NbtWriter(NbtTagType type = NbtTagType.Compound) : this(new MemoryStream(), type) { }

        public NbtWriter(Stream outstream, NbtTagType type = NbtTagType.Compound) : base(outstream)
        {
            this.parentType = type;
        }

        public void WriteCompoundStart(string name = "")
        {
            this.Validate(name, NbtTagType.Compound);

            this.Write((byte)NbtTagType.Compound);
            if (!string.IsNullOrEmpty(name))
                this.Write(name);
        }

        public void WriteListStart(string name, NbtTagType listType)
        {

        }

        public void WriteString(string name, string value)
        {
            this.Validate(name, NbtTagType.String);

            this.Write((byte)NbtTagType.String);
            this.Write(name);
            this.Write(value);
        }

        public void EndCompound()
        {
            this.Write((byte)NbtTagType.End);
        }

        public void Validate(string name, NbtTagType type)
        {
            if (this.IsClosed)
                throw new InvalidOperationException("Cannot write any more tags. Parent tag has been closed.");

            if (string.IsNullOrEmpty(name) && this.parentType == NbtTagType.Compound)
                throw new ArgumentException("Tags inside a compound tag must have a name.");

            if (this.parentType == NbtTagType.List && this.listType != type)
                throw new InvalidOperationException($"Expected list type: {this.listType}. Got: {type}");
            else if (!string.IsNullOrEmpty(name) && this.listType != null)
                throw new InvalidOperationException("Tags inside lists must be nameless.");
        }

        #region overrides
        public override void Write(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                this.Write((byte)0);
            }
            else
            {
                if (value.Length > short.MaxValue)
                    throw new InvalidOperationException($"value length must be less than {short.MaxValue}");

                var buffer = Encoding.UTF8.GetBytes(value);

                this.Write((short)buffer.Length);
                this.Write(buffer);
            }
        }

        public override void Write(byte value)
        {
            Span<byte> buffer = stackalloc byte[1];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.Write(buffer);
        }

        public override void Write(short value)
        {
            Span<byte> buffer = stackalloc byte[2];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.Write(buffer);
        }

        public override void Write(int value)
        {
            Span<byte> buffer = stackalloc byte[4];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.Write(buffer);
        }

        public override void Write(float value)
        {
            Span<byte> buffer = stackalloc byte[4];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.Write(buffer);
        }

        public override void Write(long value)
        {
            Span<byte> buffer = stackalloc byte[8];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.Write(buffer);
        }

        public override void Write(double value)
        {
            Span<byte> buffer = stackalloc byte[8];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.Write(buffer);
        }
        #endregion
    }
}
