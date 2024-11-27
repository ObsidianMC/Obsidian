namespace Obsidian.Entities;

[MinecraftEntity("minecraft:horse")]
public partial class Horse : AbstractHorse
{
    public int Variant { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(18, EntityMetadataType.VarInt);
        writer.WriteVarInt(Variant);
    }
}
