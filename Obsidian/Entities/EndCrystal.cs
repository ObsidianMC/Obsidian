using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:end_crystal")]
public sealed partial class EndCrystal : Entity
{
    private Vector? beamTarget;
    public VectorF BeamTarget => beamTarget is { } target ? new VectorF(target.X, target.Y, target.Z) : default;
    public bool ShowBottom { get; internal set; } = true;
    private bool destroyed;
    internal bool Invulnerable { get; set; }

    public EndCrystal()
    {
        Type = EntityType.EndCrystal;
        NoGravity = true;
        IsFireImmune = true;
    }

    public override async ValueTask DamageAsync(IEntity source, float amount = 1)
    {
        if (destroyed || Invulnerable || source is EnderDragon || source.Level != Level || !float.IsFinite(amount) || amount <= 0)
            return;
        destroyed = true;
        Health = 0;
        // Remove first so chained explosions cannot detonate the same crystal twice.
        await RemoveAsync();
        foreach (var dragon in Level.GetEntitiesInRange(Position, 256).OfType<EnderDragon>().ToArray())
            await dragon.OnCrystalDestroyedAsync(this, source);
        if (Level is AbstractLevel level)
        {
            await level.AbortDragonResurrectionAsync(this);
            await level.ExplodeAsync(this, 6, source);
        }
    }

    public override async ValueTask TickAsync()
    {
        if (destroyed || Level.DimensionName != "minecraft:the_end" ||
            Level is not AbstractLevel level || !level.IsMobTicking(Position)) return;
        var position = (Vector)Position.Floor();
        if (new MobTerrain(Level).GetBlock(position)?.IsAir == true)
            await level.SetBlockAsync(position, BlocksRegistry.Get(Material.Fire), true);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.OptionalBlockPos);
        writer.WriteOptional(beamTarget);
        writer.WriteEntityMetadataType(9, EntityMetadataType.Boolean);
        writer.WriteBoolean(ShowBottom);
    }

    internal void SetBeam(Vector? target)
    {
        if (Nullable.Equals(beamTarget, target)) return;
        beamTarget = target;
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEntityDataPacket { EntityId = EntityId, Entity = this });
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<bool>("ShowBottom", ShowBottom));
        tag.Set(new NbtTag<bool>("Invulnerable", Invulnerable));
        tag.Remove("BeamTarget");
        if (beamTarget is { } target)
        {
            var beam = new NbtCompound("BeamTarget");
            beam.Set(new NbtTag<int>("X", target.X));
            beam.Set(new NbtTag<int>("Y", target.Y));
            beam.Set(new NbtTag<int>("Z", target.Z));
            tag.Set(beam);
        }
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        Invulnerable = tag.TryGetBool("Invulnerable", out var invulnerable) && invulnerable;
        ShowBottom = !tag.TryGetBool("ShowBottom", out var bottom) || bottom;
        if (tag.TryGetTag<NbtCompound>("BeamTarget", out var beam) &&
            beam.TryGetTagValue<int>("X", out var x) && beam.TryGetTagValue<int>("Y", out var y) &&
            beam.TryGetTagValue<int>("Z", out var z)) beamTarget = new Vector(x, y, z);
        else if (tag.TryGetTag<NbtArray<int>>("beam_target", out var generatedBeam) && generatedBeam.Count == 3)
        {
            var values = generatedBeam.GetArray();
            beamTarget = new Vector(values[0], values[1], values[2]);
        }
    }
}
