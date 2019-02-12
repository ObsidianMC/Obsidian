//https://wiki.vg/Protocol#Packet_format
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class Packet
    {
        public int PacketId { get; internal set; }

        public int PacketLength { get; internal set; }

        public byte[] PacketData { get; internal set; }

        public Packet(int packetid, byte[] packetdata)
        {
            this.PacketData = packetdata;
            this.PacketId = packetid;
            this.PacketLength = packetid.GetVarintLength() + packetdata.Length;
        }

        internal Packet()
        {
            // Only for the static method to _not_ error
        }

        public virtual async Task WriteToStreamAsync(Stream stream)
        {
            await stream.WriteVarIntAsync(PacketLength);
            await stream.WriteVarIntAsync(PacketId);
            await stream.WriteAsync(PacketData, 0, PacketData.Length);
        }

        public static async Task<Packet> ReadFromStreamAsync(Stream stream)
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

            var packetstream = new MemoryStream(data);
            var packetid = await packetstream.ReadVarIntAsync();

            int arlen = 0;
            if (len - packetid.GetVarintLength() > -1)
                arlen = len - packetid.GetVarintLength();

            var thedata = new byte[arlen];
            await packetstream.ReadAsync(thedata, 0, thedata.Length);

            packetstream.Dispose();

            return new Packet()
            {
                PacketId = packetid,
                PacketLength = len,
                PacketData = thedata
            };
        }
    }
}