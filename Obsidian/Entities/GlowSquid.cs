using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:glow_squid")]
public sealed partial class GlowSquid : Squid
{
    public GlowSquid() => Type = EntityType.GlowSquid;
    public int DarkTicks { get; internal set; }
    protected override string? SoundName => "glow_squid";
    protected override ParticleType InkParticleType => ParticleType.GlowSquidInk;
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (DarkTicks > 0 && --DarkTicks == 0)
            SynchronizeMetadata();
    }
    protected override async ValueTask OnHurtAsync(IEntity source)
    {
        await base.OnHurtAsync(source);
        DarkTicks = 100;
        SynchronizeMetadata();
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (!IsBaby) DropItem(Material.GlowInkSac, Random.Next(1, 4));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(DarkTicks);
    }
    protected override void WriteAdditionalSave(INbtWriter writer) => writer.WriteInt("DarkTicksRemaining", DarkTicks);
    protected override void ReadAdditionalSave(NbtCompound tag) => DarkTicks = Math.Clamp((tag.TryGetTagValue<int>("DarkTicksRemaining", out var savedDarkTicksRemaining) ? savedDarkTicksRemaining : 0), 0, 100);
}
