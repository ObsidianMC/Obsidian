namespace Obsidian.Entities;

// Cube mobs share the two ageable flags followed by their size in protocol 777.
public class CubeMob : AgeableMob
{
    protected const byte CubeMetadataCount = AgeableMetadataCount + 1;

    public virtual int Size { get; set; } = 1;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(AgeableMetadataCount, EntityMetadataType.VarInt);
        writer.WriteVarInt(Size);
    }
}
