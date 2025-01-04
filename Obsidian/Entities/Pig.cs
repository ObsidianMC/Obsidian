namespace Obsidian.Entities;

[MinecraftEntity("minecraft:pig")]
public sealed partial class Pig : Animal
{
    public bool HasSaddle { get; set; }

    public int TotalTimeBoost { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean);
        writer.WriteBoolean(HasSaddle);

        writer.WriteEntityMetadataType(18, EntityMetadataType.VarInt);
        writer.WriteVarInt(TotalTimeBoost);
    }
}
