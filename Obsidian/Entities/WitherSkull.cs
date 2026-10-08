namespace Obsidian.Entities;

using Obsidian.Nbt;
using Obsidian.WorldData;

[MinecraftEntity("minecraft:wither_skull")]
public partial class WitherSkull : Arrow
{
    public bool Invulnerable { get; private set; }
    private VectorD acceleration;
    public WitherSkull() { Type = EntityType.WitherSkull; NoGravity = true; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal WitherSkull(Wither owner, VectorD position, VectorD direction, bool dangerous)
    {
        Level = owner.Level;
        Owner = owner;
        EntityId = Server.GetNextEntityId();
        Type = EntityType.WitherSkull;
        Position = position;
        NoGravity = true;
        Invulnerable = dangerous;
        if (direction.Magnitude > 0.0001) direction /= direction.Magnitude;
        acceleration = direction * 0.1;
        Motion = direction * 0.2;
        BoundingBox = Dimension.CreateBBFromPosition(position);
    }

    public override async ValueTask TickAsync()
    {
        if (Level is not AbstractLevel level || !level.IsMobTicking(Position)) return;
        Motion += acceleration;
        await base.TickAsync();
    }
    protected override double GetDrag(bool water) => Invulnerable ? 0.73 : 0.95;

    protected override async ValueTask OnImpactAsync(IEntity? target, bool damaged)
    {
        if (damaged && target is Living living)
        {
            var duration = Level.LevelData.Difficulty switch { Difficulty.Normal => 200, Difficulty.Hard => 800, _ => 0 };
            if (duration > 0) living.AddPotionEffect((int)PotionEffect.Wither - 1, duration, 1);
            if (!living.Alive && Owner is Living owner) owner.Health = Math.Min(owner.GetAttributeValue("minecraft:generic.max_health"), owner.Health + 5);
        }
        if (Level is AbstractLevel level) await level.ExplodeAsync(this, 1, Owner);
        await RemoveAsync();
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.Boolean); writer.WriteBoolean(Invulnerable);
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<bool>("dangerous", Invulnerable));
        tag.Set(EntityNbt.DoubleList("ObsidianAcceleration", acceleration));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        Invulnerable = tag.TryGetBool("dangerous", out var dangerous) && dangerous;
        if (EntityNbt.TryReadVector(tag, "ObsidianAcceleration", out var saved)) acceleration = saved;
        NoGravity = true;
    }
}
