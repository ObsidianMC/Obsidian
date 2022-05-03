using Obsidian.Utilities;
using System.Collections.Generic;
using Xunit;

namespace Obsidian.Tests.Utilities;

public sealed class NumericExtensionsTests
{
    public static IEnumerable<object[]> VarInts()
    {
        //                          varint value
        //                          |        expected byte length
        //                          |        |
        //                          v        v
        yield return new object[] { 0,       1 };
        yield return new object[] { 1 << 0,  1 };
        yield return new object[] { 1 << 6,  1 };
        yield return new object[] { 1 << 7,  2 };
        yield return new object[] { 1 << 13, 2 };
        yield return new object[] { 1 << 14, 3 };
        yield return new object[] { 1 << 20, 3 };
        yield return new object[] { 1 << 21, 4 };
        yield return new object[] { 1 << 27, 4 };
        yield return new object[] { 1 << 28, 5 };
        yield return new object[] { -1,      5 };
    }

    [Theory, MemberData(nameof(VarInts))]
    public void GetVarIntLength(int number, int expectedLength)
    {
        Assert.Equal(number.GetVarIntLength(), expectedLength);
    }
}
