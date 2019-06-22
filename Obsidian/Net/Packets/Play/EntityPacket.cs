using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class EntityPacket : Packet
    {
        public int Id { get; set; }

        public EntityPacket() : base(0x27, new byte[0]) { }

        public override Task PopulateAsync()
        {
            throw new NotImplementedException();
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using(var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(this.Id);
                return stream.ToArray();
            }
        }
    }
}
