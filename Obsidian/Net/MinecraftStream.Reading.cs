using Newtonsoft.Json;
using Obsidian.API;
using Obsidian.Chat;
using Obsidian.Serializer.Attributes;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net
{
    public partial class MinecraftStream
    {

        [ReadMethod]
        public sbyte ReadSignedByte() => (sbyte)this.ReadUnsignedByte();

        public async Task<sbyte> ReadByteAsync() => (sbyte)await this.ReadUnsignedByteAsync();

        [ReadMethod]
        public byte ReadUnsignedByte()
        {
            Span<byte> buffer = stackalloc byte[1];
            BaseStream.Read(buffer);
            return buffer[0];
        }

        public async Task<byte> ReadUnsignedByteAsync()
        {
            var buffer = new byte[1];
            await this.ReadAsync(buffer);
            return buffer[0];
        }

        [ReadMethod]
        public bool ReadBoolean()
        {
            var value = (int)this.ReadUnsignedByte();
            if (value == 0x00)
            {
                return false;
            }
            else if (value == 0x01)
            {
                return true;
            }
            else
            {
                throw new ArgumentOutOfRangeException($"Byte: {value} returned by stream is out of range (0x00 or 0x01)", nameof(BaseStream));
            }
        }

        public async Task<bool> ReadBooleanAsync()
        {
            var value = (int)await this.ReadByteAsync();
            if (value == 0x00)
            {
                return false;
            }
            else if (value == 0x01)
            {
                return true;
            }
            else
            {
                throw new ArgumentOutOfRangeException("Byte returned by stream is out of range (0x00 or 0x01)", nameof(BaseStream));
            }
        }

        [ReadMethod]
        public ushort ReadUnsignedShort()
        {
            Span<byte> buffer = stackalloc byte[2];
            this.Read(buffer);
            if (BitConverter.IsLittleEndian)
            {
                buffer.Reverse();
            }
            return BitConverter.ToUInt16(buffer);
        }

        public async Task<ushort> ReadUnsignedShortAsync()
        {
            var buffer = new byte[2];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt16(buffer);
        }

        [ReadMethod]
        public short ReadShort()
        {
            Span<byte> buffer = stackalloc byte[2];
            this.Read(buffer);
            if (BitConverter.IsLittleEndian)
            {
                buffer.Reverse();
            }
            return BitConverter.ToInt16(buffer);
        }

        public async Task<short> ReadShortAsync()
        {
            var buffer = new byte[2];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToInt16(buffer);
        }

        [ReadMethod]
        public int ReadInt()
        {
            Span<byte> buffer = stackalloc byte[4];
            this.Read(buffer);
            if (BitConverter.IsLittleEndian)
            {
                buffer.Reverse();
            }
            return BitConverter.ToInt32(buffer);
        }

        public async Task<int> ReadIntAsync()
        {
            var buffer = new byte[4];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToInt32(buffer);
        }

        [ReadMethod]
        public long ReadLong()
        {
            Span<byte> buffer = stackalloc byte[8];
            this.Read(buffer);
            if (BitConverter.IsLittleEndian)
            {
                buffer.Reverse();
            }
            return BitConverter.ToInt64(buffer);
        }

        public async Task<long> ReadLongAsync()
        {
            var buffer = new byte[8];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToInt64(buffer);
        }

        [ReadMethod]
        public ulong ReadUnsignedLong()
        {
            Span<byte> buffer = stackalloc byte[8];
            this.Read(buffer);
            if (BitConverter.IsLittleEndian)
            {
                buffer.Reverse();
            }
            return BitConverter.ToUInt64(buffer);
        }

        public async Task<ulong> ReadUnsignedLongAsync()
        {
            var buffer = new byte[8];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt64(buffer);
        }

        [ReadMethod]
        public float ReadFloat()
        {
            Span<byte> buffer = stackalloc byte[4];
            this.Read(buffer);
            if (BitConverter.IsLittleEndian)
            {
                buffer.Reverse();
            }
            return BitConverter.ToSingle(buffer);
        }

        public async Task<float> ReadFloatAsync()
        {
            var buffer = new byte[4];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToSingle(buffer);
        }

        [ReadMethod]
        public double ReadDouble()
        {
            Span<byte> buffer = stackalloc byte[8];
            this.Read(buffer);
            if (BitConverter.IsLittleEndian)
            {
                buffer.Reverse();
            }
            return BitConverter.ToDouble(buffer);
        }

        public async Task<double> ReadDoubleAsync()
        {
            var buffer = new byte[8];
            await this.ReadAsync(buffer);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToDouble(buffer);
        }

        [ReadMethod]
        public string ReadString(int maxLength = 32767)
        {
            var length = ReadVarInt();
            var buffer = new byte[length];
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            this.Read(buffer, 0, length);

            var value = Encoding.UTF8.GetString(buffer);
            if (maxLength > 0 && value.Length > maxLength)
            {
                throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));
            }
            return value;
        }

        public async Task<string> ReadStringAsync(int maxLength = 32767)
        {
            var length = await this.ReadVarIntAsync();
            var buffer = new byte[length];
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            await this.ReadAsync(buffer, 0, length);

            var value = Encoding.UTF8.GetString(buffer);
            if (maxLength > 0 && value.Length > maxLength)
            {
                throw new ArgumentException($"string ({value.Length}) exceeded maximum length ({maxLength})", nameof(value));
            }
            return value;
        }

        [ReadMethod, VarLength]
        public int ReadVarInt()
        {
            int numRead = 0;
            int result = 0;
            byte read;
            do
            {
                read = this.ReadUnsignedByte();
                int value = read & 0b01111111;
                result |= value << (7 * numRead);

                numRead++;
                if (numRead > 5)
                {
                    throw new InvalidOperationException("VarInt is too big");
                }
            } while ((read & 0b10000000) != 0);

            return result;
        }

        public virtual async Task<int> ReadVarIntAsync()
        {
            int numRead = 0;
            int result = 0;
            byte read;
            do
            {
                read = await this.ReadUnsignedByteAsync();
                int value = read & 0b01111111;
                result |= value << (7 * numRead);

                numRead++;
                if (numRead > 5)
                {
                    throw new InvalidOperationException("VarInt is too big");
                }
            } while ((read & 0b10000000) != 0);

            return result;
        }

        [ReadMethod]
        public byte[] ReadUInt8Array(int length = 0)
        {
            if (length == 0)
                length = ReadVarInt();

            var result = new byte[length];
            if (length == 0)
                return result;

            int n = length;
            while (true)
            {
                n -= Read(result, length - n, n);
                if (n == 0)
                    break;
            }
            return result;
        }

        public async Task<byte[]> ReadUInt8ArrayAsync(int length = 0)
        {
            if (length == 0)
                length = await this.ReadVarIntAsync();

            var result = new byte[length];
            if (length == 0)
                return result;

            int n = length;
            while (true)
            {
                n -= await this.ReadAsync(result, length - n, n);
                if (n == 0)
                    break;
            }
            return result;
        }

        public async Task<byte> ReadUInt8Async()
        {
            int value = await this.ReadByteAsync();
            if (value == -1)
                throw new EndOfStreamException();
            return (byte)value;
        }

        [ReadMethod, VarLength]
        public long ReadVarLong()
        {
            int numRead = 0;
            long result = 0;
            byte read;
            do
            {
                read = this.ReadUnsignedByte();
                int value = (read & 0b01111111);
                result |= (long)value << (7 * numRead);

                numRead++;
                if (numRead > 10)
                {
                    throw new InvalidOperationException("VarLong is too big");
                }
            } while ((read & 0b10000000) != 0);

            return result;
        }

        public async Task<long> ReadVarLongAsync()
        {
            int numRead = 0;
            long result = 0;
            byte read;
            do
            {
                read = await this.ReadUnsignedByteAsync();
                int value = (read & 0b01111111);
                result |= (long)value << (7 * numRead);

                numRead++;
                if (numRead > 10)
                {
                    throw new InvalidOperationException("VarLong is too big");
                }
            } while ((read & 0b10000000) != 0);

            return result;
        }

        [ReadMethod]
        public Position ReadPosition()
        {
            ulong value = this.ReadUnsignedLong();

            long x = (long)(value >> 38);
            long y = (long)(value & 0xFFF);
            long z = (long)(value << 26 >> 38);

            if (x >= Math.Pow(2, 25))
                x -= (long)Math.Pow(2, 26);

            if (y >= Math.Pow(2, 11))
                y -= (long)Math.Pow(2, 12);

            if (z >= Math.Pow(2, 25))
                z -= (long)Math.Pow(2, 26);

            return new Position
            {
                X = x,

                Y = y,

                Z = z,
            };
        }

        [ReadMethod, Absolute]
        public Position ReadAbsolutePosition()
        {
            return new Position
            {
                X = this.ReadDouble(),
                Y = this.ReadDouble(),
                Z = this.ReadDouble()
            };
        }

        public async Task<Position> ReadAbsolutePositionAsync()
        {
            return new Position
            {
                X = await this.ReadDoubleAsync(),
                Y = await this.ReadDoubleAsync(),
                Z = await this.ReadDoubleAsync()
            };
        }

        [ReadMethod]
        public SoundPosition ReadSoundPosition() => new SoundPosition(this.ReadInt(), this.ReadInt(), this.ReadInt());

        [ReadMethod]
        public Angle ReadAngle() => new Angle(this.ReadUnsignedByte());

        public async Task<Angle> ReadAngleAsync() => new Angle(await this.ReadUnsignedByteAsync());

        [ReadMethod]
        public ChatMessage ReadChat()
        {
            string value = ReadString();
            return JsonConvert.DeserializeObject<ChatMessage>(value);
        }

        [ReadMethod]
        public byte[] ReadByteArray()
        {
            var length = ReadVarInt();
            return ReadUInt8Array(length);
        }

        [ReadMethod]
        public Guid ReadGuid()
        {
            return Guid.Parse(ReadString());
        }
    }
}
