using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class PluginMessage : Packet
    {
        public string Channel { get; private set; }

        public byte[] Data { get; private set; }
        public PluginMessage() : base(0x0E, new byte[0])
        {
        }

        public async override Task PopulateAsync()
        {
            using(var stream = new MinecraftStream(this.PacketData))
            {
                this.Channel = await stream.ReadIdentifierAsync();

                //this.Data = await stream.ReadUInt8ArrayAsync();
            }
        }

        public async override Task<byte[]> ToArrayAsync()
        {
            using(var stream = new MinecraftStream())
            {
                await stream.WriteIdentifierAsync(Channel);
                await stream.WriteAsync(Data);
                return stream.ToArray();
            }
        }
    }
}