using Obsidian.Util;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    /// <summary>
    /// https://wiki.vg/Protocol#Packet_format
    /// </summary>
    public class Packet
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

            stream.semaphore.WaitOne();
            using var dataStream = new MinecraftStream();
            await ComposeAsync(dataStream);

            int packetLength = (int)dataStream.Length + this.PacketId.GetVarintLength();
            await stream.WriteVarIntAsync(packetLength);
            await stream.WriteVarIntAsync(PacketId);

            dataStream.Position = 0;
            await dataStream.CopyToAsync(stream);
            stream.semaphore.Release();
        }

        public async Task ReadAsync(byte[] data = null)
        {
            //TODO: Please look into this.
            using var stream = new MinecraftStream(data ?? this.PacketData);
            await PopulateAsync(stream);
        }

        protected virtual Task ComposeAsync(MinecraftStream stream) => Task.CompletedTask;

        protected virtual Task PopulateAsync(MinecraftStream stream) => Task.CompletedTask;
    }

    public class EmptyPacket : Packet
    {
        public EmptyPacket(int packetId, byte[] data) : base(packetId, data)
        {
        }
    }
}