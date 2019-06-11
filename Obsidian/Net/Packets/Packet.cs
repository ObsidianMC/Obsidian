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

        internal Packet() { /* Only for the static method to _not_ error */ }

        public async Task FillPacketDataAsync() => this.PacketData = await this.ToArrayAsync();

        public static async Task<T> CreateAsync<T>(T packet, MinecraftStream stream = null) where T : Packet
        {
            if (packet.Empty)
            {
                await packet.FillPacketDataAsync();
            }
            else
            {
                await packet.PopulateAsync();
            }

            if (stream != null)
                await packet.WriteToStreamAsync(stream);

            return (T)Convert.ChangeType(packet, typeof(T));
        }

        public static async Task<Packet> ReadFromStreamAsync(MinecraftStream stream)
        {
            int length = await stream.ReadVarIntAsync();
            byte[] receivedData = new byte[length];

            await stream.ReadAsync(receivedData, 0, length);

            int packetId = 0;
            byte[] packetData = new byte[0];

            using (var packetStream = new MinecraftStream(receivedData))
            {
                try
                {
                    packetId = await packetStream.ReadVarIntAsync();
                    int arlen = 0;

                    if (length - packetId.GetVarintLength() > -1)
                        arlen = length - packetId.GetVarintLength();

                    packetData = new byte[arlen];
                    await packetStream.ReadAsync(packetData, 0, packetData.Length);
                }
                catch
                {
                    throw;
                }
            }

#if PACKETLOG
            await Program.PacketLogger.LogMessageAsync($">> 0x{packetId.ToString("x")}");
#endif

            return new EmptyPacket(packetId, packetData);
        }

        public Task SendPacketAsync(MinecraftStream stream) => this.WriteToStreamAsync(stream); 

        public virtual async Task WriteToStreamAsync(MinecraftStream stream)
        {
#if PACKETLOG
            await Program.PacketLogger.LogMessageAsync($"<< 0x{this.PacketId.ToString("x")}");
#endif

            int packetLength = this.PacketData.Length + this.PacketId.GetVarintLength();

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
        public EmptyPacket(int packetId, byte[] data) : base(packetId, data) { }

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