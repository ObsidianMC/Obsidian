using Obsidian.Nbt;

namespace Obsidian.Entities;

public class AgeableMob : PathfinderMob
{
    public bool IsBaby { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsBaby);
    }

    // Obsidian doesn't age mobs, so a baby is saved with the age vanilla gives new babies.
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(new NbtTag<int>("Age", this.IsBaby ? -24000 : 0));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        this.IsBaby = tag.TryGetTag<NbtTag<int>>("Age", out var age) && age.Value < 0;
    }
}
