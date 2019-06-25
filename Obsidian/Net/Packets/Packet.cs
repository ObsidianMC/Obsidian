using Obsidian.Util;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    /// <summary>
    /// https://wiki.vg/Protocol#Packet_format
    /// </summary>
    public abstract class Packet
    {
        public byte[] PacketData { get; internal set; }

        public int PacketId { get; }

        public bool Empty => this.PacketData == null || this.PacketData.Length == 0;

        public Packet(int packetid) => this.PacketId = packetid;

        public Packet(int packetid, byte[] data)
        {
            this.PacketId = packetid;
            this.PacketData = data;
        }

        internal Packet()
        {
            /* Only for the static method to _not_ error */
        }

        public async Task FillPacketDataAsync() => this.PacketData = await this.ToArrayAsync();

        public async Task WriteToStreamAsync(MinecraftStream stream)
        {
            int packetLength = this.PacketData.Length + this.PacketId.GetVarintLength();

#if PACKETLOG
            Program.PacketLogger.LogDebug($"<< 0x{PacketId.ToString("X")}, length {packetLength}");
#endif

            byte[] data = this.PacketData;

            await stream.WriteVarIntAsync(packetLength);
            await stream.WriteVarIntAsync(PacketId);
            await stream.WriteAsync(data);
        }

        public abstract Task<byte[]> ToArrayAsync();

        public abstract Task PopulateAsync();
    }

    public class EmptyPacket : Packet
    {
        public EmptyPacket(int packetId, byte[] data) : base(packetId, data)
        {
        }

        public override Task PopulateAsync()
        {
            throw new NotImplementedException();
        }

        public override Task<byte[]> ToArrayAsync()
        {
            throw new NotImplementedException();
        }
    }
}