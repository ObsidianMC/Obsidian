using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class AnimationServerPacket : Packet
    {
        public Hand Hand { get; set; }

        public AnimationServerPacket(byte[] data) : base(0x27, data)
        {
        }

        protected override Task ComposeAsync(MinecraftStream stream) => throw new NotImplementedException();

        protected override async Task PopulateAsync(MinecraftStream stream) => this.Hand = (Hand)await stream.ReadVarIntAsync();
    }

    public enum Hand
    {
        MainHand = 0,
        OffHand = 1
    }
}