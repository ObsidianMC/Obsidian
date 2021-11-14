using System;

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

internal static class TypeExtensions
{
    public static bool IsArray(this NbtTagType type) => type == NbtTagType.ByteArray || type == NbtTagType.IntArray || type == NbtTagType.LongArray;
}
