using Obsidian.Nbt;

namespace Obsidian.Entities;

public class Mob : Living
{
    public MobBitmask MobBitMask { get; set; } = MobBitmask.None;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(15, EntityMetadataType.Byte);
        writer.WriteByte((byte)MobBitMask);
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(new NbtTag<bool>("LeftHanded", this.MobBitMask.HasFlag(MobBitmask.LeftHanded)));
        tag.SetFlag("NoAI", this.MobBitMask.HasFlag(MobBitmask.NoAi));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetBool("LeftHanded", out var leftHanded) && leftHanded)
            this.MobBitMask |= MobBitmask.LeftHanded;
        if (tag.TryGetBool("NoAI", out var noAi) && noAi)
            this.MobBitMask |= MobBitmask.NoAi;
    }
}

[Flags]
public enum MobBitmask
{
    None = 0x00,
    NoAi = 0x01,
    LeftHanded = 0x02,
    Agressive = 0x04
}
