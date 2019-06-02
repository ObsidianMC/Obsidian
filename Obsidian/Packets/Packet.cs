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
    public abstract class Packet
    {
        internal protected byte[] _packetData;
        public int PacketId { get; internal set; }

        public Packet(int packetid)
        {
            this.PacketId = packetid;
        }

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
            /*int length = await stream.ReadVarIntAsync();
            int packetId = int.MaxValue;
            byte[] data = new byte[0];
            
            if (length > 0)
            {
                packetId = await stream.ReadVarIntAsync();

                int dataLength = length - packetId.GetVarintLength();
                data = new byte[dataLength];
            }

            await stream.ReadAsync(data, 0, data.Length);

            return new Packet()
            {
                PacketId = packetId,
                PacketLength = length,
                PacketData = data
            };*/

            var len = await stream.ReadVarIntAsync();
            var data = new byte[len];
            await stream.ReadAsync(data, 0, len);

            int packetid = 0;
            byte[] thedata = new byte[0];

            if (key != null)
            {
                using (var aesStream = new AesStream(stream, key))
                {
                    packetid = await aesStream.ReadVarIntAsync();
                    int arlen = 0;
                    if (len - packetid.GetVarintLength() > -1)
                        arlen = len - packetid.GetVarintLength();

                    thedata = new byte[arlen];
                    await aesStream.ReadAsync(thedata, 0, thedata.Length);
                }
            }
            else
            {
                using (var packetstream = new MemoryStream(data))
                {
                    packetid = await packetstream.ReadVarIntAsync();
                    int arlen = 0;
                    if (len - packetid.GetVarintLength() > -1)
                        arlen = len - packetid.GetVarintLength();

                    thedata = new byte[arlen];
                    await packetstream.ReadAsync(thedata, 0, thedata.Length);
                }
            }


            await Program.PacketLogger.LogMessageAsync($">> {packetid.ToString("x")}");

            return new EmptyPacket()
            {
                PacketId = packetid,
                _packetData = thedata
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