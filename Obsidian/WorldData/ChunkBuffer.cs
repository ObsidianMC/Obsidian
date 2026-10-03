using Obsidian.Nbt;

namespace Obsidian.WorldData;
public readonly struct ChunkBuffer : IEquatable<ChunkBuffer>
{
    public required ReadOnlyMemory<byte> Memory { get; init; }

    public required NbtCompression Compression { get; init; }

    // Equal when wrapping the same memory region (ReadOnlyMemory semantics), not when the bytes match.
    public bool Equals(ChunkBuffer other) => this.Memory.Equals(other.Memory) && this.Compression == other.Compression;

    public override bool Equals(object? obj) => obj is ChunkBuffer other && this.Equals(other);

    public override int GetHashCode() => HashCode.Combine(this.Memory, this.Compression);

    public static bool operator ==(ChunkBuffer left, ChunkBuffer right) => left.Equals(right);

    public static bool operator !=(ChunkBuffer left, ChunkBuffer right) => !left.Equals(right);
}
