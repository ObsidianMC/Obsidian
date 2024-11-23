using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class InteractPacket
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

    public override void Populate(INetStreamReader reader)
    {
        this.EntityId = reader.ReadVarInt();
        this.Type = reader.ReadVarInt<InteractionType>();

        if (this.Type == InteractionType.InteractAt)
            this.Target = reader.ReadAbsoluteFloatPositionF();

        if (this.Type is InteractionType.Interact or InteractionType.InteractAt)
            this.Hand = reader.ReadVarInt<Hand>();

        this.Sneaking = reader.ReadBoolean();
    }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        var entity = player.GetEntitiesNear(4).FirstOrDefault(x => x.EntityId == EntityId); // TODO check if the entity is within range and in vision/not being blocked by a wall

        switch (Type)
        {
            case InteractionType.Interact:
                await server.EventDispatcher.ExecuteEventAsync(new EntityInteractEventArgs(player, entity, server, Sneaking));
                break;

            case InteractionType.Attack:
                await server.EventDispatcher.ExecuteEventAsync(new PlayerAttackEntityEventArgs(player, entity, server, Sneaking));
                break;

            case InteractionType.InteractAt:
                await server.EventDispatcher.ExecuteEventAsync(new EntityInteractEventArgs(player, entity, server, Hand, Target, Sneaking));
                break;
        }
    }
}
