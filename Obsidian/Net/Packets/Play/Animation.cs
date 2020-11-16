using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class Animation : IPacket
    {
        [Field(0, Type = Serializer.Enums.DataType.VarInt)]
        public Hand Hand { get; set; }

        public int Id => 0x2C;

        public Animation() { }


        public Task WriteAsync(MinecraftStream stream)
        {
            throw new System.NotImplementedException();
        }

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Hand = (Hand)await stream.ReadVarIntAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            //TODO broadcast entity animation to nearby players
            switch (this.Hand)
            {
                case Hand.MainHand:
                    await server.BroadcastPacketAsync(new EntityAnimation
                    {
                        EntityId = player.EntityId,
                        Animation = EAnimation.SwingMainArm
                    }, player);
                    break;
                case Hand.OffHand:
                    await server.BroadcastPacketAsync(new EntityAnimation
                    {
                        EntityId = player.EntityId,
                        Animation = EAnimation.SwingOffhand
                    }, player);
                    break;
                default:
                    break;
            }
        }
    }
}