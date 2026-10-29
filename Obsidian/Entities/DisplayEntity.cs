namespace Obsidian.Entities;

// Default block/item display metadata. Editable displays and their persistence remain unimplemented.
public sealed class DisplayEntity : Entity
{
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        for (var i = 0; i < 3; i++)
        {
            writer.WriteEntityMetadataType((byte)(8 + i), EntityMetadataType.VarInt);
            writer.WriteVarInt(0);
        }

        writer.WriteEntityMetadataType(11, EntityMetadataType.Vector3);
        WriteVector(writer, 0, 0, 0);
        writer.WriteEntityMetadataType(12, EntityMetadataType.Vector3);
        WriteVector(writer, 1, 1, 1);
        for (var i = 0; i < 2; i++)
        {
            writer.WriteEntityMetadataType((byte)(13 + i), EntityMetadataType.Quaternion);
            WriteVector(writer, 0, 0, 0);
            writer.WriteSingle(1);
        }

        writer.WriteEntityMetadataType(15, EntityMetadataType.Byte);
        writer.WriteByte(0);
        writer.WriteEntityMetadataType(16, EntityMetadataType.VarInt);
        writer.WriteVarInt(-1);
        // View range, shadow radius/strength, width and height, in protocol order.
        ReadOnlySpan<float> values = [1, 0, 1, 0, 0];
        for (var i = 0; i < values.Length; i++)
        {
            writer.WriteEntityMetadataType((byte)(17 + i), EntityMetadataType.Float);
            writer.WriteSingle(values[i]);
        }
        writer.WriteEntityMetadataType(22, EntityMetadataType.VarInt);
        writer.WriteVarInt(-1);

        if (Type == EntityType.BlockDisplay)
        {
            writer.WriteEntityMetadataType(23, EntityMetadataType.BlockState);
            writer.WriteVarInt(0); // Air.
        }
        else
        {
            writer.WriteEntityMetadataType(23, EntityMetadataType.Slot);
            writer.WriteItemStack(null);
            writer.WriteEntityMetadataType(24, EntityMetadataType.Byte);
            writer.WriteByte(0);
        }
    }

    private static void WriteVector(INetStreamWriter writer, float x, float y, float z)
    {
        writer.WriteSingle(x);
        writer.WriteSingle(y);
        writer.WriteSingle(z);
    }
}
