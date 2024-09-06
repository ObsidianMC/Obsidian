namespace Obsidian.Nbt;

public enum NbtTagType : byte
{
    End,
    Byte,
    Short,
    Int,
    Long,
    Float,
    Double,
    ByteArray,
    String,
    List,
    Compound,
    IntArray,
    LongArray,

    Unknown = 255
}
