namespace Obsidian.Serializer.Enums
{
    public enum DataType
    {
        Auto,
        Boolean,
        Byte,
        UnsignedByte,
        Short,
        UnsignedShort,
        Int,
        Long,
        Float,
        Double,
        String,
        Chat,
        Identifier,
        VarInt,
        VarLong,
        EntityMetadata,
        Slot,
        NbtTag,
        /// <summary>
        /// Position serialized as an Int64.
        /// </summary>
        Position,
        /// <summary>
        /// Position serialized as three double values.
        /// </summary>
        AbsolutePosition,
        Transform,
        SoundPosition,
        Angle,
        UUID,
        Array,
        Velocity,
        ByteArray,
        // TODO: Implement missing data types
        // Optional X
        // Array of X
        // X Enum
        // Byte Array
    }
}