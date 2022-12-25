using Obsidian.Nbt;

namespace Obsidian.WorldData;
public readonly struct ChunkBuffer
{
    public required ReadOnlyMemory<byte> Memory { get; init; }

    public required NbtCompression Compression { get; init; }
}
