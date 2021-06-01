using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class InteractEntity : IServerboundPacket
    {
        public int EntityId { get; set; }

        public InteractionType Type { get; set; }

        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public float TargetZ { get; set; }

        public Hand Hand { get; set; }

        public bool Sneaking { get; set; }

        public int Id => 0x0E;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            var entity = player.GetEntitiesNear(4).FirstOrDefault(x => x.EntityId == this.EntityId);//TODO check if the entity is within range and in vision/not being blocked by a wall

            switch (this.Type)
            {
                case InteractionType.Interact:
                    await server.Events.InvokeEntityInteractAsync(new EntityInteractEventArgs(player, entity, server, this.Sneaking));
                    break;
                case InteractionType.Attack:
                    await server.Events.InvokePlayerAttackEntityAsync(new PlayerAttackEntityEventArgs(player, entity, server, this.Sneaking));
                    break;
                case InteractionType.InteractAt:
                    var pos = new VectorF(this.TargetX, this.TargetY, this.TargetZ);
                    await server.Events.InvokeEntityInteractAsync(new EntityInteractEventArgs(player, entity, server, this.Hand, pos, this.Sneaking));
                    break;
                default:
                    break;
            }
        }

        public void Populate(byte[] data)
        {
            using var stream = new MinecraftStream(data);
            Populate(stream);
        }

        public void Populate(MinecraftStream stream)
        {
            this.EntityId = stream.ReadVarInt();
            this.Type = (InteractionType)stream.ReadVarInt();

            if (this.Type == InteractionType.InteractAt)
            {
                this.TargetX = stream.ReadFloat();
                this.TargetY = stream.ReadFloat();
                this.TargetZ = stream.ReadFloat();
            }

            if (this.Type == InteractionType.Interact || this.Type == InteractionType.InteractAt)
                this.Hand = (Hand)stream.ReadVarInt();

            this.Sneaking = stream.ReadBoolean();
        }
    }
}
