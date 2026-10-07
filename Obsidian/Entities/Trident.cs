namespace Obsidian.Entities;

[MinecraftEntity("minecraft:trident")]
public sealed partial class Trident : Arrow
{
    public int LoyaltyLevel { get; private set; }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(11, EntityMetadataType.Byte);
        writer.WriteByte((byte)LoyaltyLevel);
        writer.WriteEntityMetadataType(12, EntityMetadataType.Boolean);
        writer.WriteBoolean(false);
    }
}
