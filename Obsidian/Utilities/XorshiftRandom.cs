using System;
using System.Runtime.CompilerServices;

namespace Obsidian.Utilities;

public struct XorshiftRandom
{
    private const double Unit = 1.0 / (int.MaxValue + 1.0);
    private const float FloatUnit = 1f / uint.MaxValue;
    private const double DoubleUnit = 1.0 / ulong.MaxValue;

    private ulong stateA;
    private ulong stateB;

    public XorshiftRandom(long seed)
    {
        stateA = (ulong)seed;
        stateB = (ulong)seed >> 32;
    }

    public int Next()
    {
        var value = Step();
        return (int)(value & 0xFFFFFFFF);
    }

    public int Next(int maxValue)
    {
        if (maxValue > 1)
        {
            return (int)(Unit * NextInt32() * maxValue);
        }

        return 0;
    }

    public int Next(int minValue, int maxValue)
    {
        uint exclusiveRange = (uint)(maxValue - minValue);

        if (exclusiveRange > 1)
        {
            return minValue + (int)(Unit * NextInt32() * exclusiveRange);
        }

        return minValue;
    }

    public double NextDouble()
    {
        return DoubleUnit * Step();
    }

    public ulong NextUlong()
    {
        return Step();
    }

    public float NextFloat()
    {
        return FloatUnit * NextUInt32();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int NextInt32()
    {
        ulong step = Step();
        return Unsafe.As<ulong, int>(ref step) & int.MaxValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint NextUInt32()
    {
        ulong step = Step();
        return Unsafe.As<ulong, uint>(ref step);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static XorshiftRandom Create()
    {
        return new(Environment.TickCount64 ^ long.MinValue);
    }

    /// <summary>
    /// Increases the RNG by one step and returns it's ulong value.
    /// https://en.wikipedia.org/wiki/Xorshift#xorshift+                                                                                           
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong Step()
    {
        ulong t = stateA;
        ulong s = stateB;

        stateA = s;
        t ^= t << 23;
        t ^= t >> 17;
        t ^= s ^ (s >> 26);
        stateB = t;
        return t + s;
    }
}
