using Obsidian.Nbt;

namespace Obsidian.Entities;

public class AgeableMob : PathfinderMob
{
    private bool legacyBaby;
    public int Age { get; internal set; }
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

        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsBaby);
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
            Age = tag.TryGetTag<NbtTag<int>>("Age", out var age) ? age.Value : 0;
        else
            this.IsBaby = tag.TryGetTag<NbtTag<int>>("Age", out var legacyAge) && legacyAge.Value < 0;
    }
}
