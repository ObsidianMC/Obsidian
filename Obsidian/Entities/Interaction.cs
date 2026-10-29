namespace Obsidian.Entities;

[MinecraftEntity("minecraft:interaction")]
public sealed partial class Interaction : Entity
{
    public Interaction() => Type = EntityType.Interaction;

    public float Width { get; set; } = 1;
    public float Height { get; set; } = 1;
    public bool Response { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.Float);
        writer.WriteSingle(Width);
        writer.WriteEntityMetadataType(8 + 1, EntityMetadataType.Float);
        writer.WriteSingle(Height);
        writer.WriteEntityMetadataType(8 + 2, EntityMetadataType.Boolean);
        writer.WriteBoolean(Response);
    }
}
