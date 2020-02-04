using Obsidian.Util;
using SharpCompress.Compressors.Deflate;
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

        public int PacketId { get; internal set; }

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

        public int GetPacketLength() => this.PacketId.GetVarintLength() + this.PacketData.Length;

        public virtual async Task WriteAsync(MinecraftStream stream)
        {
            stream.semaphore.WaitOne();
            using var dataStream = new MinecraftStream();
            await ComposeAsync(dataStream);

            var packetLength = this.PacketId.GetVarintLength() + (int)dataStream.Length;

            await stream.WriteVarIntAsync(packetLength);
            await stream.WriteVarIntAsync(PacketId);

            dataStream.Position = 0;
            await dataStream.CopyToAsync(stream);
            stream.semaphore.Release();
        }

        public virtual async Task WriteCompressedAsync(MinecraftStream stream, int threshold = 0)
        {
            using var memstr = new MinecraftStream();
            await memstr.WriteVarIntAsync(PacketId);
            await this.ComposeAsync(memstr);

            var dataLength = this.PacketId.GetVarintLength() + (int)memstr.Length;
            var useCompression = threshold > 0 && dataLength >= threshold;

            if (!useCompression)
            {
                Console.WriteLine($"Not compressing");
                await this.WriteAsync(stream);
            }
            else
            {
                //compress
                Console.WriteLine($"Compressing");
                var compdata = ZLibUtils.Compress(memstr.ToArray());

                var packetLength = dataLength + compdata.Length;

                await stream.WriteVarIntAsync(packetLength);
                await stream.WriteVarIntAsync(dataLength);

                await stream.WriteAsync(compdata, 0, compdata.Length);
            }
        }

        public virtual async Task ReadAsync(byte[] data = null)
        {
            //TODO: Please look into this.
            using var stream = new MinecraftStream(data ?? this.PacketData);
            await PopulateAsync(stream);
        }

        protected virtual Task ComposeAsync(MinecraftStream stream) => Task.CompletedTask;

        protected virtual Task PopulateAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}