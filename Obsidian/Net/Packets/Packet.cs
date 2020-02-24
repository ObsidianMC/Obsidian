using Obsidian.Util;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    /// <summary>
    /// https://wiki.vg/Protocol#Packet_format
    /// </summary>
    public class Packet
    {
        public bool Empty => this.packetData == null || this.packetData.Length == 0;

        internal byte[] packetData;

        internal int packetId;

        public Packet(int packetid) => this.packetId = packetid;

        public Packet(int packetId, byte[] data) => (this.packetData, this.packetId) = (data, packetId);

        public virtual async Task WriteAsync(MinecraftStream stream)
        {
            await using var dataStream = new MinecraftStream();
            await ComposeAsync(dataStream);

            var packetLength = this.packetId.GetVarintLength() + (int)dataStream.Length;

            await stream.WriteVarIntAsync(packetLength);
            await stream.WriteVarIntAsync(packetId);

            dataStream.Position = 0;
            await dataStream.CopyToAsync(stream);
        }

        public virtual async Task WriteCompressedAsync(MinecraftStream stream, int threshold = 0)
        {
            await using var dataStream = new MinecraftStream();
            await ComposeAsync(dataStream);

            var dataLength = this.packetId.GetVarintLength() + (int)dataStream.Length;
            var useCompression = threshold > 0 && dataLength >= threshold;

            dataStream.Position = 0;

            if (useCompression)
            {
                Console.WriteLine("compressing");
                await using var memoryStream = new MemoryStream();
                await ZLibUtils.WriteCompressedAsync(dataStream, memoryStream);

                var packetLength = dataLength + (int)memoryStream.Length;

                await stream.WriteVarIntAsync(packetLength);
                await stream.WriteVarIntAsync(dataLength);
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(stream);
            }
            else
            {
                Console.WriteLine("Not compressing");
                await stream.WriteVarIntAsync(0);
                await stream.WriteVarIntAsync(this.packetId);
                await dataStream.CopyToAsync(stream);
            }
        }

        public virtual async Task ReadAsync(byte[] data = null)
        {
            //TODO: Please look into this.
            using var stream = new MinecraftStream(data ?? this.packetData);
            await PopulateAsync(stream);
        }

        protected virtual Task ComposeAsync(MinecraftStream stream) => Task.CompletedTask;

        protected virtual Task PopulateAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}