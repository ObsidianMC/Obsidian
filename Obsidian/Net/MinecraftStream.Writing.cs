using Obsidian.API;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net
{
    public partial class MinecraftStream
    {
        [WriteMethod(DataType.Byte)]
        public void WriteByte(sbyte value)
        {
            BaseStream.WriteByte((byte)value);
        }
        
        public async Task WriteByteAsync(sbyte value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Byte (0x{value.ToString("X")})");
#endif

            await this.WriteUnsignedByteAsync((byte)value);
        }

        [WriteMethod(DataType.UnsignedByte)]
        public void WriteUnsignedByte(byte value)
        {
            BaseStream.WriteByte(value);
        }

        public async Task WriteUnsignedByteAsync(byte value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing unsigned Byte (0x{value.ToString("X")})");
#endif

            await this.WriteAsync(new[] { value });
        }

        [WriteMethod(DataType.Boolean)]
        public void WriteBoolean(bool value)
        {
            BaseStream.WriteByte(value ? 0x01 : 0x00);
        }

        public async Task WriteBooleanAsync(bool value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Boolean ({value})");
#endif

            await this.WriteByteAsync((sbyte)(value ? 0x01 : 0x00));
        }

        [WriteMethod(DataType.UnsignedShort)]
        public void WriteUnsignedShort(ushort value)
        {
            Span<byte> span = stackalloc byte[2];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteUnsignedShortAsync(ushort value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing unsigned Short ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod(DataType.Short)]
        public void WriteShort(short value)
        {
            Span<byte> span = stackalloc byte[2];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteShortAsync(short value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Short ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod(DataType.Int)]
        public void WriteInt(int value)
        {
            Span<byte> span = stackalloc byte[4];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteIntAsync(int value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Int ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod(DataType.Long)]
        public void WriteLong(long value)
        {
            Span<byte> span = stackalloc byte[8];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteLongAsync(long value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Long ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod(DataType.Float)]
        public void WriteFloat(float value)
        {
            Span<byte> span = stackalloc byte[4];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteFloatAsync(float value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Float ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod(DataType.Double)]
        public void WriteDouble(double value)
        {
            Span<byte> span = stackalloc byte[8];
            BitConverter.TryWriteBytes(span, value);
            if (BitConverter.IsLittleEndian)
            {
                span.Reverse();
            }
            BaseStream.Write(span);
        }

        public async Task WriteDoubleAsync(double value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing Double ({value})");
#endif

            var write = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(write);
            }
            await this.WriteAsync(write);
        }

        [WriteMethod(DataType.String)]
        public void WriteString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(bytes.Length);
            Write(bytes);
        }

        public async Task WriteStringAsync(string value, int maxLength = short.MaxValue)
        {
            //await Globals.PacketLogger.LogDebugAsync($"Writing String ({value})");

            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (value.Length > maxLength)
                throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));

            var bytes = Encoding.UTF8.GetBytes(value);
            await this.WriteVarIntAsync(bytes.Length);
            await this.WriteAsync(bytes);
        }

        [WriteMethod(DataType.VarInt)]
        public void WriteVarInt(int value)
        {
            var unsigned = (uint)value;

            do
            {
                var temp = (byte)(unsigned & 127);
                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;

                BaseStream.WriteByte(temp);
            }
            while (unsigned != 0);
        }

        public async Task WriteVarIntAsync(int value)
        {
            //await Globals.PacketLogger.LogDebugAsync($"Writing VarInt ({value})");

            var unsigned = (uint)value;

            do
            {
                var temp = (byte)(unsigned & 127);

                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;

                await this.WriteUnsignedByteAsync(temp);
            }
            while (unsigned != 0);
        }

        public void WriteVarInt(Enum value)
        {
            WriteVarInt(Convert.ToInt32(value));
        }

        /// <summary>
        /// Writes a "VarInt Enum" to the specified <paramref name="stream"/>.
        /// </summary>
        public async Task WriteVarIntAsync(Enum value) => await this.WriteVarIntAsync(Convert.ToInt32(value));

        public async Task WriteLongArrayAsync(long[] values)
        {
            foreach (var value in values)
                await this.WriteLongAsync(value);
        }

        public async Task WriteLongArrayAsync(ulong[] values)
        {
            foreach (var value in values)
                await this.WriteLongAsync((long)value);
        }

        [WriteMethod(DataType.VarLong)]
        public void WriteVarLong(long value)
        {
            var unsigned = (ulong)value;

            do
            {
                var temp = (byte)(unsigned & 127);

                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;


                BaseStream.WriteByte(temp);
            }
            while (unsigned != 0);
        }

        public async Task WriteVarLongAsync(long value)
        {
#if PACKETLOG
            await Globals.PacketLogger.LogDebugAsync($"Writing VarLong ({value})");
#endif

            var unsigned = (ulong)value;

            do
            {
                var temp = (byte)(unsigned & 127);

                unsigned >>= 7;

                if (unsigned != 0)
                    temp |= 128;


                await this.WriteUnsignedByteAsync(temp);
            }
            while (unsigned != 0);
        }

        [WriteMethod(DataType.Angle)]
        public void WriteAngle(Angle angle)
        {
            BaseStream.WriteByte(angle.Value);
        }

        public async Task WriteAngleAsync(Angle angle)
        {
            await this.WriteByteAsync((sbyte)angle.Value);
            // await this.WriteUnsignedByteAsync((byte)(angle / Angle.MaxValue * byte.MaxValue));
        }
    }
}
