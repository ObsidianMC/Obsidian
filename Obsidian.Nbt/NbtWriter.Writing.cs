using System;
using System.Linq;
using System.Text;

namespace Obsidian.Nbt
{
    public partial class NbtWriter
    {
        private void Write(NbtTagType tagType) => this.WriteByte((byte)tagType);

        public void WriteByte(byte value) => this.BaseStream.WriteByte(value);

        public void WriteString(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (value.Length > short.MaxValue)
                throw new InvalidOperationException($"value length must be less than {short.MaxValue}");

            var buffer = Encoding.UTF8.GetBytes(value);

            this.WriteShort((short)buffer.Length);
            this.BaseStream.Write(buffer);
        }

        public void WriteShort(short value)
        {
            Span<byte> buffer = stackalloc byte[2];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        public void WriteInt(int value)
        {
            Span<byte> buffer = stackalloc byte[4];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        public void WriteFloat(float value)
        {
            Span<byte> buffer = stackalloc byte[4];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        public void WriteLong(long value)
        {
            Span<byte> buffer = stackalloc byte[8];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        public void WriteDouble(double value)
        {
            Span<byte> buffer = stackalloc byte[8];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }
    }
}
