using Obsidian.Utilities;
using System;
using System.Collections.Generic;
using Xunit;

namespace Obsidian.Tests.Utilities
{
    public class GuidHelperTests
    {
        public static IEnumerable<object[]> Data => new string[][]
        {
            new[] { string.Empty },
            new[] { "ABCDE" },
            new[] { "0123456789ABCDEF" }
        };

        [Fact(DisplayName = "Passing null throws an exception")]
        public void NullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => GuidHelper.FromStringHash(null));
            Assert.Throws<ArgumentNullException>(() => GuidHelper.FromStringHashCryptographic(null));
        }

        [MemberData(nameof(Data))]
        [Theory(DisplayName = "Creating GUID from string hash")]
        public void GuidFromStringHash(string text)
        {
            Guid guid = GuidHelper.FromStringHash(text);
            Assert.NotEqual(Guid.Empty, guid);

            Guid guid2 = GuidHelper.FromStringHash(text);
            Assert.Equal(guid, guid2);

            Guid guid3 = GuidHelper.FromStringHash(text + "_");
            Assert.NotEqual(guid, guid3);
        }

        [MemberData(nameof(Data))]
        [Theory(DisplayName = "Creating GUID from string hash using cryptography")]
        public void GuidFromStringHashCryptographic(string text)
        {
            Guid guid = GuidHelper.FromStringHashCryptographic(text);
            Assert.NotEqual(Guid.Empty, guid);

            Guid guid2 = GuidHelper.FromStringHashCryptographic(text);
            Assert.Equal(guid, guid2);

            Guid guid3 = GuidHelper.FromStringHashCryptographic(text + "_");
            Assert.NotEqual(guid, guid3);
        }
    }
}
