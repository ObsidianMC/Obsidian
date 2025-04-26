using Obsidian.API;

using Xunit;

namespace Obsidian.Tests;

public class BitSetTest
{
    [Fact(DisplayName = "BitSet Tests")]
    public void TestGetSet()
    {
        var bs = new BitSet();
        bs.SetBit(63, true);
        bs.SetBit(65, true);

        Assert.False(bs.GetBit(0));
        Assert.False(bs.GetBit(12));
        Assert.False(bs.GetBit(62));
        Assert.True(bs.GetBit(63));
        Assert.False(bs.GetBit(64));
        Assert.True(bs.GetBit(65));
        Assert.False(bs.GetBit(66));
        Assert.False(bs.GetBit(125));
    }
}
