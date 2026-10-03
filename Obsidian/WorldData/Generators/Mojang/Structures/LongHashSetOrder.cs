namespace Obsidian.WorldData.Generators.Mojang.Structures;

/// <summary>
/// The iteration order of fastutil's <c>LongOpenHashSet</c> (default capacity, load factor 0.75), which decides the order
/// vanilla places the starts of a structure that reference the same chunk.
/// </summary>
internal static class LongHashSetOrder
{
    /// <summary>
    /// The distinct <paramref name="values"/> in the order a <c>LongOpenHashSet</c> they were added to (in order) iterates.
    /// </summary>
    public static List<long> Order(IEnumerable<long> values)
    {
        var size = 0;
        var containsZero = false;
        var keys = new long[32];
        var mask = 31;
        var maxFill = 24;

        foreach (var value in values)
        {
            if (value == 0L)
            {
                if (!containsZero)
                {
                    containsZero = true;
                    size = Grow(ref keys, ref mask, ref maxFill, size);
                }

                continue;
            }

            var position = (int)Mix(value) & mask;
            var duplicate = false;
            while (keys[position] != 0L)
            {
                if (keys[position] == value)
                {
                    duplicate = true;
                    break;
                }

                position = (position + 1) & mask;
            }

            if (duplicate)
                continue;

            keys[position] = value;
            size = Grow(ref keys, ref mask, ref maxFill, size);
        }

        // The iterator returns the zero key first, then walks the table from its end.
        var order = new List<long>(size);
        if (containsZero)
            order.Add(0L);

        for (var position = keys.Length - 1; position >= 0; position--)
        {
            if (keys[position] != 0L)
                order.Add(keys[position]);
        }

        return order;
    }

    /// <summary>
    /// Counts an added key and, like fastutil (<c>if (size++ &gt;= maxFill)</c>), doubles the table when it was full.
    /// </summary>
    private static int Grow(ref long[] keys, ref int mask, ref int maxFill, int size)
    {
        if (size++ < maxFill)
            return size;

        // fastutil's rehash moves the keys from the end of the old table to the new one.
        var newKeys = new long[keys.Length * 2];
        var newMask = newKeys.Length - 1;
        for (var index = keys.Length - 1; index >= 0; index--)
        {
            if (keys[index] == 0L)
                continue;

            var position = (int)Mix(keys[index]) & newMask;
            while (newKeys[position] != 0L)
                position = (position + 1) & newMask;

            newKeys[position] = keys[index];
        }

        keys = newKeys;
        mask = newMask;
        maxFill = Math.Min((int)Math.Ceiling(newKeys.Length * 0.75), newKeys.Length - 1);
        return size;
    }

    /// <summary>fastutil's <c>HashCommon.mix(long)</c>.</summary>
    private static long Mix(long value)
    {
        var hash = unchecked(value * -7046029254386353131L);
        hash ^= (long)((ulong)hash >> 32);
        return hash ^ (long)((ulong)hash >> 16);
    }
}
