using Obsidian.Entities;
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

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    public void EqualityUsesEntityIds(int firstId, int secondId, bool equal)
    {
        var first = new Entity { Level = null!, EntityId = firstId };
        var second = new Entity { Level = null!, EntityId = secondId };

        Assert.Equal(equal, first == second);
        Assert.Equal(!equal, first != second);
    }
}
