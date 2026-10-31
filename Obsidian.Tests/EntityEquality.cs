using Obsidian.Entities;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

public sealed class EntityEquality
{
    [Fact]
    public void NullComparisonsAreSymmetric()
    {
        Entity? missing = null;
        var entity = new Entity { Level = null! };
        Entity? alsoMissing = null;

        Assert.False(missing == entity);
        Assert.False(entity == missing);
        Assert.True(missing != entity);
        Assert.True(entity != missing);
        Assert.True(missing == alsoMissing);
        Assert.False(missing != alsoMissing);
    }

    [Fact]
    public void EqualityUsesEntityIds()
    {
        (int FirstId, int SecondId, bool Equal)[] cases =
        [
            (1, 1, true),
            (1, 2, false)
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"firstId={row.FirstId}, secondId={row.SecondId}, equal={row.Equal}",
            row => AssertEqualityUsesEntityIds(row.FirstId, row.SecondId, row.Equal));
    }

    private static void AssertEqualityUsesEntityIds(int firstId, int secondId, bool equal)
    {
        var first = new Entity { Level = null!, EntityId = firstId };
        var second = new Entity { Level = null!, EntityId = secondId };

        Assert.Equal(equal, first == second);
        Assert.Equal(!equal, first != second);
    }
}
