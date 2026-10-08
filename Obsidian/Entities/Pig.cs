namespace Obsidian.Entities;

[MinecraftEntity("minecraft:pig")]
public sealed partial class Pig : Animal
{
    public bool HasSaddle { get; set; }

    public int TotalTimeBoost { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        // Saddles use equipment now; field 17 is boost time, followed by the client's default variant.
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(TotalTimeBoost);
    }
}
