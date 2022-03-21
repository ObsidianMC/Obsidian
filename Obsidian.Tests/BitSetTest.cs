using Obsidian.API._Types;
using Obsidian.Net;

using System.Collections.Generic;

using Xunit;

namespace Obsidian.Tests;

public class BitSetTest
{
    [Fact(DisplayName = "BitSet Tests", Timeout = 100)]
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
