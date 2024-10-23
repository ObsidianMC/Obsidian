using System.Runtime.CompilerServices;

namespace Obsidian.Utilities;

public static class NumericsHelper
{
    private static readonly ConcurrentDictionary<long, PackedXZ> packedXZCache = [];
    private static readonly ConcurrentDictionary<PackedXZ, long> unpackedXZCache = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LongToInts(long l, out int a, out int b)
    {
        if (packedXZCache.TryGetValue(l, out var cachedResult))
        {
            a = cachedResult.X;
            b = cachedResult.Z;
            return;
        }

        a = (int)(l & uint.MaxValue);
        b = (int)(l >> 32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long IntsToLong(int a, int b)
    {
        if (unpackedXZCache.TryGetValue((a, b), out var packedXZ))
            return packedXZ;

        packedXZ = ((long)b << 32) | (uint)a;

        packedXZCache.TryAdd(packedXZ, new PackedXZ { X = a, Z = b });

        return packedXZ;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Modulo(int x, int mod) => (x % mod + mod) % mod;

    private readonly record struct PackedXZ
    {
        public required int X { get; init; }
        public required int Z { get; init; }

        public static implicit operator PackedXZ((int x, int z) tuple) => new() { X = tuple.x, Z = tuple.z };
    }
}
