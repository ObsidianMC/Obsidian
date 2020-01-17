using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using Obsidian.Util;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public abstract class CompressedPacket : Packet
    {
        protected CompressedPacket(int packetid, byte[] packetData) : base(packetid, packetData) { }

        private CompressedPacket()
        {
            /* Only for the static method to _not_ error*/
        }


        public async Task WriteToStreamAsync(MinecraftStream stream)
        {
            var packetLength = this.PacketId.GetVarintLength() + this.PacketData.Length;
            // compress data
            using var memstr = new MinecraftStream();

            await memstr.WriteVarIntAsync(PacketId);
            await memstr.WriteAsync(this.PacketData);

            using var inflate = new InflaterInputStream(memstr);
            var compdata = new byte[inflate.Length];
            await inflate.ReadAsync(compdata, 0, compdata.Length);

            await stream.WriteVarIntAsync(packetLength);
            await stream.WriteVarIntAsync(compdata.Length);
            await stream.WriteAsync(compdata, 0, compdata.Length);
        }

        public static async Task ReadFromStreamAsync(Stream stream)
        {
            using var mstream = new MinecraftStream(stream);

            var len = await mstream.ReadVarIntAsync();
            var dataLen = await mstream.ReadVarIntAsync();

            // read compressed data
            var compdata = new byte[len - dataLen.GetVarintLength()];
            await stream.ReadAsync(compdata, 0, len);

            // decompress data
            var data = new byte[dataLen];
            using var memstr = new MemoryStream(data);
            using var deflate = new DeflaterOutputStream(memstr);
            await deflate.WriteAsync(compdata, 0, compdata.Length);
            memstr.Position = 0;
            var packetId = await mstream.ReadVarIntAsync();

            var arlen = 0;
            if (dataLen - packetId.GetVarintLength() > -1)
                arlen = len - packetId.GetVarintLength();

            var theData = new byte[arlen];
            await memstr.ReadAsync(theData, 0, theData.Length);
        }
    }
}