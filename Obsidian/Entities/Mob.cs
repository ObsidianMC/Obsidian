namespace Obsidian.Entities;

public class Mob : Living
{
    public MobBitmask MobBitMask { get; set; } = MobBitmask.None;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(14, EntityMetadataType.Byte);
        writer.WriteByte((byte)MobBitMask);
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
