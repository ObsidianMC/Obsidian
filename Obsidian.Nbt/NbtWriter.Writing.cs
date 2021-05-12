using System;
using System.Linq;
using System.Text;

namespace Obsidian.Nbt
{
    public partial class NbtWriter
    {
        private void Write(NbtTagType tagType) => this.Write((byte)tagType);

        private void Write(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            if (value.Length > short.MaxValue)
                throw new InvalidOperationException($"value length must be less than {short.MaxValue}");

            var buffer = Encoding.UTF8.GetBytes(value);

            this.Write((short)buffer.Length);
            this.BaseStream.Write(buffer);
        }

        private void Write(short value)
        {
            Span<byte> buffer = stackalloc byte[2];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        private void Write(int value)
        {
            Span<byte> buffer = stackalloc byte[4];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        private void Write(float value)
        {
            Span<byte> buffer = stackalloc byte[4];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        private void Write(long value)
        {
            Span<byte> buffer = stackalloc byte[8];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }

        private void Write(double value)
        {
            Span<byte> buffer = stackalloc byte[8];

            BitConverter.TryWriteBytes(buffer, value);

            if (BitConverter.IsLittleEndian)
                buffer.Reverse();

            this.BaseStream.Write(buffer);
        }
    }
}
