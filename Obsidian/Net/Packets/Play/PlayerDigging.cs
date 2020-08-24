using System.Threading.Tasks;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play
{
    public class PlayerDigging : Packet
    {
        public int Status { get; private set; }
        public Position Location { get; private set; }
        public sbyte Face { get; private set; } // This is an enum of what face of the block is being hit

        public PlayerDigging(byte[] packetdata) : base(0x18, packetdata)
        {
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync(Status);
            await stream.WritePositionAsync(Location);
            await stream.WriteByteAsync(Face);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            this.Status = await stream.ReadVarIntAsync();
            this.Location = await stream.ReadPositionAsync();
            this.Face = await stream.ReadByteAsync();
        }
    }
}