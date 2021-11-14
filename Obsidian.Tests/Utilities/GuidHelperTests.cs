using Obsidian.Utilities;
using System;
using Xunit;

namespace Obsidian.Tests.Utilities;

public class GuidHelperTests
{
    [Fact(DisplayName = "Passing null throws an exception")]
    public void NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => GuidHelper.FromStringHash(null));
    }

    [Theory(DisplayName = "Getting version from GUID")]
    [InlineData(0, "00000000-0000-0000-0000-000000000000")]
    [InlineData(1, "29dafd90-c321-11eb-8529-0242ac130003")]
    [InlineData(3, "1e0ca5b1-252f-3f6b-9e0a-c91be7e7219e")]
    public void GetVersion(int expectedVersion, string guid)
    {
        Assert.Equal(expectedVersion, GuidHelper.GetVersion(new Guid(guid)));
    }

    [Theory(DisplayName = "Getting variant of GUID")]
    [InlineData(0, "00000000-0000-0000-0000-000000000000")]
    [InlineData(1, "29dafd90-c321-11eb-8529-0242ac130003")]
    [InlineData(1, "1e0ca5b1-252f-3f6b-9e0a-c91be7e7219e")]
    public void GetVariant(int expectedVariant, string guid)
    {
        Assert.Equal(expectedVariant, GuidHelper.GetVariant(new Guid(guid)));
    }

    [Theory(DisplayName = "Creating GUID from string hash")]
    [InlineData("")]
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
