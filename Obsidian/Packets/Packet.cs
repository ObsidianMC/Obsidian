//https://wiki.vg/Protocol#Packet_format
using Obsidian.Util;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    /// <summary>
    /// https://wiki.vg/Protocol#Packet_format
    /// </summary>
    public abstract class Packet
    {
        internal protected byte[] _packetData;
        public int PacketId { get; internal set; }

        public Packet(int packetid) => this.PacketId = packetid;

        public Packet(int packetid, byte[] data)
        {
            this.PacketId = packetid;
            this._packetData = data;
        }

        internal Packet() { /* Only for the static method to _not_ error */ }

        internal async Task FillPacketDataAsync() => this._packetData = await this.ToArrayAsync();


        public bool Empty => this._packetData == null || this._packetData.Length == 0;

        public static async Task<Packet> ReadFromStreamAsync(Stream stream, byte[] key = null)
        {
            int length = await stream.ReadVarIntAsync();
            byte[] receivedData = new byte[length];

            await stream.ReadAsync(receivedData, 0, length);

            int packetId = 0;
            byte[] packetData = new byte[0];

            using (var packetStream = new MemoryStream(receivedData))
            {
                Stream readStream = packetStream;

                if (key != null)
                    readStream = new AesStream(stream, key);

                try
                {
                    packetId = await readStream.ReadVarIntAsync();
                    int arlen = 0;

                    if (length - packetId.GetVarintLength() > -1)
                        arlen = length - packetId.GetVarintLength();

                    packetData = new byte[arlen];
                    await readStream.ReadAsync(packetData, 0, packetData.Length);
                }
                catch
                {
                    throw;
                }
                finally //To still dispose the temporary stream (even with exceptions) to be sure.
                {
                    if (key != null)
                        readStream.Dispose();
                }
            }

            await Program.PacketLogger.LogMessageAsync($">> {packetId.ToString("x")}");

            return new EmptyPacket()
            {
                PacketId = packetId,
                _packetData = packetData
            };
        }

        public static async Task<T> CreateAsync<T>(T packet) where T : Packet
        {
            if (!packet.Empty)
            {
                await packet.PopulateAsync();
            }
            else
            {
                await packet.FillPacketDataAsync();
            }

            return (T)Convert.ChangeType(packet, typeof(T));
        }

        public virtual async Task WriteToStreamAsync(Stream stream, BufferedBlockCipher encrypt = null)
        {
            await Program.PacketLogger.LogMessageAsync($"<< {this.PacketId.ToString("x")}");

            var packetLength = this.PacketId.GetVarintLength() + this._packetData.Length;

            byte[] data = this._packetData;

            if (encrypt != null)
            {
                data = encrypt.ProcessBytes(this._packetData, 0, this._packetData.Length);
            }

            await stream.WriteVarIntAsync(packetLength);
            await stream.WriteVarIntAsync(PacketId);
            await stream.WriteAsync(data, 0, data.Length);
        }

        public Packet WithDataFrom(Packet p)
        {
            this._packetData = p._packetData;
            return this; // ;^)
        }

        public abstract Task<byte[]> ToArrayAsync();

        protected abstract Task PopulateAsync();

    }
    public class EmptyPacket : Packet
    {
        protected override Task PopulateAsync()
        {
            throw new NotImplementedException();
        }

        public override Task<byte[]> ToArrayAsync()
        {
            throw new NotImplementedException();
        }
    }
}