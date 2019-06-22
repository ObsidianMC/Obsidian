using Obsidian.PlayerData.Info;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class PlayerDigging : Packet
    {
        public int Status { get; private set; }
        public Position Location { get; private set; }
        public sbyte Face { get; private set; } // This is an enum of what face of the block is being hit

        public PlayerDigging(byte[] packetdata) : base(0x18, packetdata) { }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(Status);
                await stream.WritePositionAsync(Location);
                await stream.WriteByteAsync(Face);
                return stream.ToArray();
            }
        }

        public async override Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this.PacketData))
            {
                this.Status = await stream.ReadVarIntAsync();
                this.Location = await stream.ReadPositionAsync();
                this.Face = await stream.ReadByteAsync();
            }
        }
    }
}
