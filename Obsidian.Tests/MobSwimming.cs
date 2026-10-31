using Obsidian.API;
using Obsidian.API.AI;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class MobSwimming
{
    [Fact]
    public void PathNodeTypesMapBothWaysToTheGeneratedServerTypes()
    {
        var names = Enum.GetValues<PathType>().Length;
        Assert.Equal(names, Enum.GetValues<PathNodeType>().Length);
        foreach (var type in Enum.GetValues<PathNodeType>())
            Assert.Equal(type, PathNodeMapping.ToPublic(PathNodeMapping.ToInternal(type)));
    }
}
