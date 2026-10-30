using Obsidian.API.World.Generator.RandomSources;
using System;
using Xunit;

namespace Obsidian.Tests;

public class ComponentNumberStreams
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(42, 100)]
    [InlineData(-7, 64)]
    public void AdapterPreservesTheSuppliedRandomStream(int seed, int bound)
    {
        var expected = new Random(seed);
        var caller = new Random(seed);
        var source = new SystemRandomSource(caller);
        var expectedInt = expected.Next(bound);
        var expectedFloat = expected.NextSingle();
        var expectedDouble = expected.NextDouble();

        var actualInt = source.NextInt(bound);
        var actualFloat = source.NextFloat();
        var actualDouble = source.NextDouble();

        Assert.Equal(expectedInt, actualInt);
        Assert.Equal(expectedFloat, actualFloat);
        Assert.Equal(expectedDouble, actualDouble);
        Assert.Equal(expected.Next(), caller.Next());
    }
}
