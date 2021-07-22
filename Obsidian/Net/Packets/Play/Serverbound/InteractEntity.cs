﻿using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class InteractEntity : IServerboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; private set; }

        [Field(1), ActualType(typeof(int)), VarLength]
        public InteractionType Type { get; private set; }

        [Field(2), DataFormat(typeof(float)), Condition("Type == InteractionType.InteractAt")]
        public VectorF Target { get; private set; }

        [Field(3), ActualType(typeof(int)), VarLength, Condition("Type is InteractionType.Interact or InteractionType.InteractAt")]
        public Hand Hand { get; private set; }

        [Field(4)]
        public bool Sneaking { get; private set; }

        public int Id => 0x0E;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            var entity = player.GetEntitiesNear(4).FirstOrDefault(x => x.EntityId == EntityId); // TODO check if the entity is within range and in vision/not being blocked by a wall

            switch (Type)
            {
                case InteractionType.Interact:
                    await server.Events.InvokeEntityInteractAsync(new EntityInteractEventArgs(player, entity, server, Sneaking));
                    break;

                case InteractionType.Attack:
                    await server.Events.InvokePlayerAttackEntityAsync(new PlayerAttackEntityEventArgs(player, entity, server, Sneaking));
                    break;

                case InteractionType.InteractAt:
                    await server.Events.InvokeEntityInteractAsync(new EntityInteractEventArgs(player, entity, server, Hand, Target, Sneaking));
                    break;
            }
        }
    }
}
