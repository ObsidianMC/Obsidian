using System;
using System.Linq;
using System.Text;

namespace Obsidian.Nbt
{
    public partial class NbtWriter
    {
        private void Write(NbtTagType tagType) => this.WriteShort((byte)tagType);

        public void WriteString(string value)
        {
            this.Validate(null, NbtTagType.String);

            if (string.IsNullOrEmpty(value))
            {
                this.WriteShort(0);
                return;
            }

            if (value.Length > short.MaxValue)
                throw new InvalidOperationException($"value length must be less than {short.MaxValue}");

            var buffer = Encoding.UTF8.GetBytes(value);

            this.WriteShort((short)buffer.Length);
            this.BaseStream.Write(buffer);
        }

        public void WriteShort(short value)
        {
            this.Validate(null, NbtTagType.Short);

            Span<byte> buffer = stackalloc byte[2];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        public void WriteInt(int value)
        {
            this.Validate(null, NbtTagType.Int);

            Span<byte> buffer = stackalloc byte[4];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        public void WriteFloat(float value)
        {
            this.Validate(null, NbtTagType.Float);

            Span<byte> buffer = stackalloc byte[4];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        public void WriteLong(long value)
        {
            this.Validate(null, NbtTagType.Long);

            Span<byte> buffer = stackalloc byte[8];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        public void WriteDouble(double value)
        {
            this.Validate(null, NbtTagType.Double);

            Span<byte> buffer = stackalloc byte[8];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }
    }
}
