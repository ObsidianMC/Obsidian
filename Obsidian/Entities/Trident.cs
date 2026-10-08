namespace Obsidian.Entities;

[MinecraftEntity("minecraft:trident")]
public sealed partial class Trident : Arrow
{
    public int LoyaltyLevel { get; internal set; }
    private bool dealtDamage;

    public override async ValueTask TickAsync()
    {
        if (Owner == null) { await base.TickAsync(); return; }
        if (LoyaltyLevel <= 0 || !dealtDamage && !embedded) { await base.TickAsync(); return; }
        if (Owner is not Player owner || !owner.Alive || owner.GameMode == GameMode.Spectator || owner.Level != Level)
        {
            if (Pickup == ArrowPickup.Allowed && PickupItem != null)
            {
                var dropped = new ItemEntity { Level = Level, EntityId = Server.GetNextEntityId(), Position = Position, Item = PickupItem };
                if (!Level.TryAddEntity(dropped)) return;
                dropped.SpawnEntity();
            }
            await RemoveAsync();
            return;
        }
        NoClip = true;
        var difference = owner.Position + new VectorD(0, 1.62, 0) - Position;
        var distance = difference.Magnitude;
        if (distance < 1.5 && PickupItem != null && await owner.ReceiveProjectileAsync(PickupItem, Pickup == ArrowPickup.CreativeOnly))
        { await RemoveAsync(); return; }
        if (distance <= 0.0001) return;
        Motion = Motion * 0.95 + difference / distance * (0.05 * LoyaltyLevel);
        var next = Position + Motion;
        if (Level is Obsidian.WorldData.AbstractLevel level && !level.TryMoveEntity(this, Position, next)) return;
        Position = next;
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new Obsidian.Net.Packets.Play.Clientbound.TeleportEntityPacket
        { EntityId = EntityId, Position = Position, Delta = Motion, Yaw = Yaw, Pitch = Pitch, OnGround = false });
    }

    protected override async ValueTask OnImpactAsync(IEntity? target, bool damaged)
    {
        dealtDamage = true;
        embedded = true;
        Motion *= -0.01;
        if (damaged && target is Living living && CombatItems.EnchantmentLevel(Weapon, EnchantmentsRegistry.Channeling) > 0 &&
            Level.LevelData.Thundering && new Obsidian.Entities.AI.MobTerrain(Level).GetSkyLight((Vector)target.Position.Floor()) == 15)
        {
            Level.SpawnEntity(target.Position, EntityType.LightningBolt);
            await living.DamageCombatAsync(Owner ?? this, 5, CombatDamageKind.Fire);
            living.Ignite(8);
        }
    }

    internal override void WriteNbt(Obsidian.Nbt.NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new Obsidian.Nbt.NbtTag<bool>("DealtDamage", dealtDamage));
        tag.Set(new Obsidian.Nbt.NbtTag<int>("ObsidianLoyalty", LoyaltyLevel));
    }

    internal override void ReadNbt(Obsidian.Nbt.NbtCompound tag)
    {
        base.ReadNbt(tag);
        dealtDamage = tag.TryGetBool("DealtDamage", out var dealt) && dealt;
        LoyaltyLevel = Weapon != null ? CombatItems.EnchantmentLevel(Weapon, EnchantmentsRegistry.Loyalty) :
            tag.TryGetTagValue<int>("ObsidianLoyalty", out var loyalty) ? Math.Clamp(loyalty, 0, 127) : 0;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(11, EntityMetadataType.Byte);
        writer.WriteByte((byte)LoyaltyLevel);
        writer.WriteEntityMetadataType(12, EntityMetadataType.Boolean);
        writer.WriteBoolean(false);
    }
}
