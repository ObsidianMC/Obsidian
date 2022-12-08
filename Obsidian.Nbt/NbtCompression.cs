namespace Obsidian.Nbt;

public enum NbtCompression : byte
{
    None,

    GZip,

    ZLib,

    Zstd,

    Brotli
}
