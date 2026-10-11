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
    public InteractionHand Hand { get; private set; }

    [Field(4)]
    public bool Sneaking { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.EntityId = reader.ReadVarInt();
        this.Type = reader.ReadVarInt<InteractionType>();

        if (this.Type == InteractionType.InteractAt)
            this.Target = reader.ReadAbsoluteFloatPositionF();

        if (this.Type is InteractionType.Interact or InteractionType.InteractAt)
            this.Hand = reader.ReadVarInt<InteractionHand>();

        this.Sneaking = reader.ReadBoolean();
    }

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        var entity = player.GetEntitiesNear(player.GameMode == GameMode.Creative ? 8 : 6).FirstOrDefault(x => x.EntityId == EntityId);

        entity ??= player.GetEntitiesNear(24).OfType<EnderDragon>()
            .SelectMany(dragon => dragon.Parts)
            .FirstOrDefault(part => part.EntityId == EntityId && part.IsInRange(player, 4));

        if (entity == null)
            return;

        if (Type is InteractionType.Interact or InteractionType.InteractAt && player.Level is Obsidian.WorldData.AbstractLevel events)
            events.EmitGameEvent(Obsidian.WorldData.MobGameEvent.EntityInteract, entity.Position, player);

        switch (Type)
        {
            case InteractionType.Interact:
                await server.EventDispatcher.ExecuteEventAsync(new EntityInteractEventArgs(player, entity, server, Hand, Sneaking));
                break;

            case InteractionType.Attack:
                var attack = new PlayerAttackEntityEventArgs(player, entity, server, Sneaking);
                if (player is Player combatPlayer)
                {
                    if (!combatPlayer.CanAttack(entity)) return;
                    (attack.Damage, attack.IsCrit) = combatPlayer.GetAttackDamage(entity);
                }
                else attack.Damage = 1;
                await server.EventDispatcher.ExecuteEventAsync(attack);
                break;

            case InteractionType.InteractAt:
                await server.EventDispatcher.ExecuteEventAsync(new EntityInteractEventArgs(player, entity, server, Hand, Target, Sneaking));
                break;
        }
    }
}
