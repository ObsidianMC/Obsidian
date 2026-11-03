using Obsidian.WorldData.Generators.Density;
using System;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Guards the generation threads' scratch stacks, which every density sample borrows from: a throw or a misordered,
/// repeated or stale disposal must never leave a buffer that's still in use free to be lent again.
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

        // Buffers rented afterwards are still distinct.
        using var outer = ScratchBuffer<float>.Rent(Volume.Size);
        using var inner = ScratchBuffer<float>.Rent(Volume.Size);

        Assert.Equal(depth + 2, stack.Depth);
        Assert.False(outer.Span.Overlaps(inner.Span));
    }

    [Fact]
    public void MisorderedDisposalNeverLendsALiveBuffer()
    {
        var stack = new ScratchStack<int>();

        // The outer buffer is given back first, while the inner one is still in use.
        var outer = stack.Rent(16);
        var inner = stack.Rent(16);
        outer.Dispose();

        // Giving back the stale buffer again, or the inner one late, must not free the new ones.
        var first = stack.Rent(16);
        var second = stack.Rent(16);
        outer.Dispose();
        inner.Dispose();
        var third = stack.Rent(16);

        Assert.Equal(3, stack.Depth);
        Assert.False(inner.Span.Overlaps(first.Span));
        Assert.False(inner.Span.Overlaps(second.Span));
        Assert.False(inner.Span.Overlaps(third.Span));
        Assert.False(first.Span.Overlaps(second.Span));
        Assert.False(second.Span.Overlaps(third.Span));
    }

    [Fact]
    public void StaleCopyOfAPooledBufferNeverReturnsItsArrayAgain()
    {
        // Longer than a stack keeps, so these come from the shared pool.
        var stack = new ScratchStack<int>();
        var length = ScratchStack<int>.MaxRetainedElements + 1;

        var first = stack.Rent(length);
        var staleCopy = first;
        first.Dispose();

        // The pool may hand the returned array out again, but the stale copy must not return it a second time.
        var second = stack.Rent(length);
        staleCopy.Dispose();
        var third = stack.Rent(length);

        Assert.Equal(0, stack.Depth);
        Assert.False(second.Span.Overlaps(third.Span));

        second.Dispose();
        third.Dispose();
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
