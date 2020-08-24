using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class EntityPacket : Packet
    {
        public int Id { get; set; }

        public EntityPacket() : base(0x27, Array.Empty<byte>())
        {
        }

        protected override async Task ComposeAsync(MinecraftStream stream) => await stream.WriteVarIntAsync(this.Id);

        protected override Task PopulateAsync(MinecraftStream stream) => throw new NotImplementedException();
    }
}