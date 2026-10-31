using Obsidian.API.World.Generator.RandomSources;
using System;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

public class ComponentNumberStreams
{
    [Fact]
    public void AdapterPreservesTheSuppliedRandomStream()
    {
        (int Seed, int Bound)[] cases =
        [
            (1, 1),
            (42, 100),
            (-7, 64)
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"seed={row.Seed}, bound={row.Bound}",
            row => AssertAdapterPreservesTheSuppliedRandomStream(row.Seed, row.Bound));
    }

    private static void AssertAdapterPreservesTheSuppliedRandomStream(int seed, int bound)
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
