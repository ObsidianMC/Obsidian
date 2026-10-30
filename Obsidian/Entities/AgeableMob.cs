using Obsidian.Nbt;

namespace Obsidian.Entities;

public class AgeableMob : PathfinderMob, IAgeable
{
    // Base-layout counts keep inherited offsets aligned without a mutable cursor during concurrent writes.
    protected const byte AgeableMetadataCount = MobMetadataCount + 2;

    private bool legacyBaby;
    public int Age { get; internal set; }
    public bool AgeLocked { get; set; }
    public virtual bool IsBaby
    {
        get => HasAi ? Age < 0 : legacyBaby;
        set
        {
            legacyBaby = value;
            Age = value ? -24000 : 0;
        }
    }

    protected override float DimensionScale => HasAi && IsBaby ? 0.5f : 1;

    protected override ValueTask TickMobAsync()
    {
        var wasBaby = IsBaby;
        Age += Math.Sign(-Age);
        if (wasBaby != IsBaby)
            SynchronizeMetadata();
        return base.TickMobAsync();
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(MobMetadataCount, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsBaby);

        writer.WriteEntityMetadataType(MobMetadataCount + 1, EntityMetadataType.Boolean);
        writer.WriteBoolean(AgeLocked);
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(new NbtTag<int>("Age", HasAi ? Age : this.IsBaby ? -24000 : 0));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (HasAi)
            Age = tag.TryGetNumber<int>("Age", out var age) ? age : 0;
        else
            this.IsBaby = tag.TryGetNumber<int>("Age", out var legacyAge) && legacyAge < 0;
    }
}
