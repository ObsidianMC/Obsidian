using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class IncomingChatMessage : Packet
    {
        public IncomingChatMessage(byte[] data) : base(0x02, data) { }

        public string Message { get; private set; }


        public override async Task Populate()
        {
            using (var stream = new MemoryStream(this._packetData))
            {
                this.Message = await stream.ReadStringAsync(256);
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using(var ms = new MemoryStream())
            {
                await ms.WriteStringAsync(this.Message);
                return ms.ToArray();
            }
        }
    }
}