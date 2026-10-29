namespace Obsidian.Entities;

[MinecraftEntity("minecraft:sulfur_cube")]
public sealed partial class SulfurCube : CubeMob
{
    public int MaxFuse { get; set; } = -1;
    public bool FromBucket { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(CubeMetadataCount, EntityMetadataType.VarInt);
        writer.WriteVarInt(MaxFuse);
        writer.WriteEntityMetadataType(CubeMetadataCount + 1, EntityMetadataType.Boolean);
        writer.WriteBoolean(FromBucket);
    }
}
