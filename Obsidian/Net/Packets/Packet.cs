using Obsidian.Util;
using System;
using System.Reflection.Metadata;
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

        public async Task WriteAsync(MinecraftStream stream)
        {
            using (var dataStream = new MinecraftStream())
            {
                await ComposeAsync(dataStream);

                int packetLength = (int)dataStream.Length + this.PacketId.GetVarintLength();
                await stream.WriteVarIntAsync(packetLength);
                await stream.WriteVarIntAsync(PacketId);

                dataStream.Position = 0;
                await dataStream.CopyToAsync(stream);
            }
        }

        public async Task ReadAsync(byte[] data)
        {
            //TODO: Please look into this.
            using (var stream = new MinecraftStream(data))
                await PopulateAsync(stream);
        }

        protected abstract Task ComposeAsync(MinecraftStream stream);

        protected abstract Task PopulateAsync(MinecraftStream stream);
    }

    public class EmptyPacket : Packet
    {
        public EmptyPacket(int packetId, byte[] data) : base(packetId, data)
        {
        }

        protected override Task PopulateAsync(MinecraftStream stream) => throw new NotImplementedException();

        protected override Task ComposeAsync(MinecraftStream stream) => throw new NotImplementedException();
    }
}