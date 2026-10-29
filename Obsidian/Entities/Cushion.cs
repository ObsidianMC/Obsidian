namespace Obsidian.Entities;

[MinecraftEntity("minecraft:cushion")]
public sealed partial class Cushion : Entity
{
    public Cushion() => Type = EntityType.Cushion;

    // Dye colors are registry-independent VarInts; zero is white.
    public int Color { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.DyeColor);
        writer.WriteVarInt(Color);
    }
}
