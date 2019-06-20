using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class AnimationServerPacket : Packet
    {
        public Hand Hand { get; set; }

        public AnimationServerPacket(byte[] data) : base(0x27, data) { }

        public async override Task PopulateAsync()
        {
            using(var stream = new MinecraftStream(this.PacketData))
            {
                this.Hand = (Hand)await stream.ReadVarIntAsync();
            }
        }

        public override Task<byte[]> ToArrayAsync()
        {
            throw new NotImplementedException();
        }
    }

    public enum Hand
    {
        MainHand = 0,
        OffHand = 1
    }
}
