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

        public async Task WriteToStreamAsync(MinecraftStream inStream, MinecraftStream outStream)
        {
            int packetLength = this.PacketData.Length + this.PacketId.GetVarintLength();

#if PACKETLOG
            Program.PacketLogger.LogDebug($"<< 0x{PacketId.ToString("X")}, length {packetLength}");
            Program.PacketLogger.LogDebug("====================================");
#endif

            //byte[] data = this.PacketData;

            await outStream.WriteVarIntAsync(packetLength);
            await outStream.WriteVarIntAsync(PacketId);
            await inStream.CopyToAsync(outStream);
        }

        public virtual Task DeserializeAsync() => Task.CompletedTask;

        public virtual Task<byte[]> SerializeAsync() => Task.FromResult(new byte[0]);
    }

    public class EmptyPacket : Packet
    {
        public EmptyPacket(int packetId, byte[] data) : base(packetId, data) { }
    }
}