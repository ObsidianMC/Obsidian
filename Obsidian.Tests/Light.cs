using Obsidian.API;
using Obsidian.ChunkData;
using Xunit;

namespace Obsidian.Tests;

public class LightTests
{
    [Fact(DisplayName = "Light Get/Set Tests")]
    public void TestGetSet()
    {
        var cs = new ChunkSection();

        cs.SetLightLevel(new Vector(1, 0, 0), LightType.Sky, 15);
        var actual = cs.GetLightLevel(new Vector(1, 0, 0), LightType.Sky);
        Assert.Equal(15, actual);

        cs.SetLightLevel(new Vector(1, 7, 0), LightType.Sky, 15);
        actual = cs.GetLightLevel(new Vector(1, 7, 0), LightType.Sky);
        Assert.Equal(15, actual);
    }
}
