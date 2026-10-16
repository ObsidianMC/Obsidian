using Obsidian.Utilities;
using System;
using Xunit;

namespace Obsidian.Tests.Utilities;

public class GuidHelperTests
{
    [Theory(DisplayName = "Creating GUID from string hash")]
    [InlineData("0123456789ABCDEF")]
    public void GuidFromStringHash(string text)
    {
        Guid guid = GuidHelper.FromStringHash(text);
        Assert.NotEqual(Guid.Empty, guid);

        Guid guid2 = GuidHelper.FromStringHash(text);
        Assert.Equal(guid, guid2);

        Guid guid3 = GuidHelper.FromStringHash(text + "_");
        Assert.NotEqual(guid, guid3);

        Assert.Equal(3, GuidHelper.GetVersion(guid));
        Assert.Equal(1, GuidHelper.GetVariant(guid));
    }
}
