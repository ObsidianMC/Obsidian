using Obsidian.Nbt;
using System.IO;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

public class Nbt(ITestOutputHelper output)
{
    [Fact]
    public void BigTest()
    {
        var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Tests.Assets.bigtest.nbt");

        var reader = new NbtReader(fs, NbtCompression.GZip);

        var main = reader.ReadNextTag() as NbtCompound;

        //Writing out the string to read ourselves
        output.WriteLine(main.ToString());

        //Begin reading

        Assert.Equal("Level", main.Name);

        Assert.Equal(NbtTagType.Compound, main.Type);

        Assert.Equal(11, main.Count);

        var longTest = main.GetLong("longTest");
        Assert.Equal(long.MaxValue, longTest);

        var shortTest = main.GetShort("shortTest");
        Assert.Equal(short.MaxValue, shortTest);

        var stringTest = main.GetString("stringTest");
        Assert.Equal("HELLO WORLD THIS IS A TEST STRING ÅÄÖ!", stringTest);

        var floatTest = main.GetFloat("floatTest");
        Assert.Equal(0.49823147058486938, floatTest);

        var intTest = main.GetInt("intTest");
        Assert.Equal(int.MaxValue, intTest);

        var byteTest = main.GetByte("byteTest");
        Assert.Equal(127, byteTest);

        var doubleTest = main.GetDouble("doubleTest");
        Assert.Equal(0.49312871321823148, doubleTest);

        main.TryGetTag<NbtArray<byte>>("byteArrayTest (the first 1000 values of (n*n*255+n*7)%100, starting with n=0 (0, 62, 34, 16, 8, ...))", out var byteArrayTest);
        Assert.Equal(1000, byteArrayTest.Count);

        for (int n = 0; n < 1000; n++)
            Assert.Equal((n * n * 255 + n * 7) % 100, byteArrayTest[n]);

        #region nested compounds
        main.TryGetTag<NbtCompound>("nested compound test", out var nestedCompound);

        Assert.Equal(2, nestedCompound.Count);

        nestedCompound.TryGetTag<NbtCompound>("ham", out var ham);

        Assert.Equal(2, ham.Count);

        Assert.Equal("Hampus", ham.GetString("name"));
        Assert.Equal(0.75, ham.GetFloat("value"));

        nestedCompound.TryGetTag<NbtCompound>("egg", out var egg);

        Assert.Equal(2, egg.Count);
        Assert.Equal("Eggbert", egg.GetString("name"));
        Assert.Equal(0.5, egg.GetFloat("value"));
        #endregion nested compounds

        #region lists
        main.TryGetTag<NbtList>("listTest (long)", out var listLongTest);

        Assert.Equal(5, listLongTest.Count);

        var count = 11;

        foreach (var tag in listLongTest)
        {
            if (tag is NbtTag<long> item)
                Assert.Equal(count++, item.Value);
        }

        main.TryGetTag<NbtList>("listTest (compound)", out var listCompoundTest);

        Assert.Equal(2, listCompoundTest.Count);

        var compound1 = listCompoundTest[0] as NbtCompound;
        Assert.Equal("Compound tag #0", compound1.GetString("name"));
        Assert.Equal(1264099775885, compound1.GetLong("created-on"));


        var compound2 = listCompoundTest[1] as NbtCompound;
        Assert.Equal("Compound tag #1", compound2.GetString("name"));
        Assert.Equal(1264099775885, compound2.GetLong("created-on"));
        #endregion lists
    }

    [Fact]
    public void TestWrite()
    {
        using var stream = new MemoryStream();
        using var writer = new NbtWriter(stream, "Test Compound");

        writer.WriteString("egg", "cheese");

        writer.WriteListStart("List Test", NbtTagType.Int, 3);

        for (int i = 0; i < 3; i++)
        {
            writer.WriteInt(i);
        }

        writer.EndList();

        writer.EndCompound();

        stream.Position = 0;

        var reader = new NbtReader(stream);

        var root = reader.ReadNextTag() as NbtCompound;

        Assert.Equal("Test Compound", root.Name);

        Assert.Equal("cheese", root.GetString("egg"));

        Assert.True(root.TryGetTag<NbtList>("List Test", out var list));

        Assert.Equal(3, list.Count);

        for (int i = 0; i < 3; i++)
        {
            var tag = list[i] as NbtTag<int>;

            Assert.Equal(i, tag.Value);
        }
    }
}
