using Obsidian.API;
using Obsidian.Nbt;
using Obsidian.Net;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

public class Nbt
{
    private bool isSetup;
    private readonly ITestOutputHelper output;

    public Nbt(ITestOutputHelper output)
    {
        this.output = output;
    }

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

        //var byteArrayTest = main.GetArr("byteArrayTest (the first 1000 values of (n*n*255+n*7)%100, starting with n=0 (0, 62, 34, 16, 8, ...))");//TODO add getting an array from a compound
        /*Assert.Equal(1000, byteArrayTest.Value.Length);

        for (int n = 0; n < 1000; n++)
            Assert.Equal((n * n * 255 + n * 7) % 100, byteArrayTest.Value[n]);*/

        #region nested compounds
        main.TryGetTag("nested compound test", out INbtTag compound);
        var nestedCompound = (NbtCompound)compound;

        Assert.Equal(2, nestedCompound.Count);

        nestedCompound.TryGetTag("ham", out INbtTag hamCompound);
        var ham = (NbtCompound)hamCompound;

        Assert.Equal(2, ham.Count);

        Assert.Equal("Hampus", ham.GetString("name"));
        Assert.Equal(0.75, ham.GetFloat("value"));

        nestedCompound.TryGetTag("egg", out INbtTag eggCompound);
        var egg = (NbtCompound)eggCompound;

        Assert.Equal(2, egg.Count);
        Assert.Equal("Eggbert", egg.GetString("name"));
        Assert.Equal(0.5, egg.GetFloat("value"));
        #endregion nested compounds

        #region lists
        main.TryGetTag("listTest (long)", out var longList);
        var listLongTest = (NbtList)longList;

        Assert.Equal(5, listLongTest.Count);

        var count = 11;

        foreach (var tag in listLongTest)
        {
            if (tag is NbtTag<long> item)
                Assert.Equal(count++, item.Value);
        }

        main.TryGetTag("listTest (compound)", out var compoundList);
        var listCompoundTest = (NbtList)compoundList;

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
    public async Task ReadSlot()
    {
        await SetupAsync();

        await using var stream = new MinecraftStream();

        var itemMeta = new ItemMetaBuilder()
            .WithName("test")
            .WithDurability(1)
            .Build();

        var material = Material.Bedrock;

        var dataSlot = new ItemStack(material, 0, itemMeta)
        {
            Present = true
        };

        await stream.WriteSlotAsync(dataSlot);

        stream.Position = 0;

        var slot = await stream.ReadSlotAsync();

        Assert.True(slot.Present);
        Assert.Equal(0, slot.Count);
        Assert.Equal(material, slot.Type);

        Assert.Equal("test", slot.ItemMeta.Name.Text);
        Assert.Equal(1, slot.ItemMeta.Durability);
    }

    private async Task SetupAsync()
    {
        if (isSetup)
            return;
        isSetup = true;
    }
}
