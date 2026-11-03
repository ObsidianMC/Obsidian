using Obsidian.WorldData.Generators.Density;
using System;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Guards the generation threads' scratch stacks, which every density sample borrows from: a throw or a misordered
/// disposal must never leave a buffer that's still in use free to be lent again.
/// </summary>
public class DensityScratchBuffers
{
    private static readonly DensityVolume Volume = new(4, 8, 4, 0, 0, 0, 1, 1, 1);

    [Fact]
    public void ThrowMidSampleLeavesTheStackBalanced()
    {
        var stack = ScratchStack<float>.Current;
        var depth = stack.Depth;

        // A function nested in a sample rents its own buffer, then throws.
        Assert.Throws<InvalidOperationException>(() => SampleOnce(new ThrowingFunction()));
        Assert.Equal(depth, stack.Depth);

        // The arrays it borrowed are lent again, rather than dropped and regrown.
        var retained = stack.Retained;
        SampleOnce(new ConstantFunction(1.0f));

        Assert.Equal(depth, stack.Depth);
        Assert.Equal(retained, stack.Retained);
    }

    [Fact]
    public void MisorderedDisposalNeverLendsALiveBuffer()
    {
        var stack = new ScratchStack<int>();

        // The outer buffer is given back first, while the inner one is still in use.
        var outer = stack.Rent(16);
        var inner = stack.Rent(16);
        inner.Span.Fill(7);
        outer.Dispose();

        // Neither new buffer may share the inner one's array.
        var first = stack.Rent(16);
        var second = stack.Rent(16);
        first.Span.Fill(1);
        second.Span.Fill(2);

        // Giving back the stale buffer again, or the inner one late, must not free the new ones.
        outer.Dispose();
        inner.Dispose();
        var third = stack.Rent(16);
        third.Span.Fill(3);

        Assert.Equal(3, stack.Depth);
        Assert.All(inner.Span.ToArray(), value => Assert.Equal(7, value));
        Assert.All(first.Span.ToArray(), value => Assert.Equal(1, value));
        Assert.All(second.Span.ToArray(), value => Assert.Equal(2, value));
    }

    private static void SampleOnce(DensityFunction function)
    {
        using var values = DensityBuffer.Sample(function, Volume);
    }

    private sealed class ThrowingFunction : DensityFunction
    {
        public override float Sample(int x, int y, int z) => throw new InvalidOperationException();

        public override void SampleVolume(Span<float> output, in DensityVolume volume)
        {
            using var nested = new DensityBuffer(volume.Size);

            throw new InvalidOperationException();
        }
    }
}
