using ICSharpCode.SharpZipLib.GZip;
using Obsidian.API;
using Obsidian.Nbt;
using Obsidian.Nbt.Tags;
using Obsidian.Net;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests
{
    public class Nbt
    {
        private readonly ITestOutputHelper output;

        public Nbt(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void BigTest()
        {
            var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Tests.Assets.bigtest.nbt");
            var decompressedStream = new MemoryStream();

            GZip.Decompress(fs, decompressedStream, false);

            decompressedStream.Position = 0;
            var reader = new NbtReader(decompressedStream);

            var main = reader.ReadAsTag() as NbtCompound;

            //Writing out the string to read ourselves
            output.WriteLine(main.ToString());

            //Begin reading

            Assert.Equal("Level", main.Name);

            Assert.Equal(NbtTagType.Compound, main.TagType);

            Assert.Equal(11, main.Count);

            var longTest = main.Get<NbtLong>("longTest");
            Assert.Equal(long.MaxValue, longTest.Value);

            var shortTest = main.Get<NbtShort>("shortTest");
            Assert.Equal(short.MaxValue, shortTest.Value);

            var stringTest = main.Get<NbtString>("stringTest");
            Assert.Equal("HELLO WORLD THIS IS A TEST STRING ÅÄÖ!", stringTest.Value);

            var floatTest = main.Get<NbtFloat>("floatTest");
            Assert.Equal(0.49823147058486938, floatTest.Value);

            var intTest = main.Get<NbtInt>("intTest");
            Assert.Equal(int.MaxValue, intTest.Value);

            var byteTest = main.Get<NbtByte>("byteTest");
            Assert.Equal(127, byteTest.Value);

            var doubleTest = main.Get<NbtDouble>("doubleTest");
            Assert.Equal(0.49312871321823148, doubleTest.Value);

            var byteArrayTest = main.Get<NbtByteArray>("byteArrayTest (the first 1000 values of (n*n*255+n*7)%100, starting with n=0 (0, 62, 34, 16, 8, ...))");
            Assert.Equal(1000, byteArrayTest.Value.Length);

            for (int n = 0; n < 1000; n++)
                Assert.Equal((n * n * 255 + n * 7) % 100, byteArrayTest.Value[n]);

            #region nested compounds
            var nestedCompound = main.Get<NbtCompound>("nested compound test");
            Assert.Equal(2, nestedCompound.Count);

            var ham = nestedCompound.Get<NbtCompound>("ham");
            Assert.Equal(2, ham.Count);

            Assert.Equal("Hampus", ham.Get<NbtString>("name").Value);
            Assert.Equal(0.75, ham.Get<NbtFloat>("value").Value);

            var egg = nestedCompound.Get<NbtCompound>("egg");
            Assert.Equal(2, egg.Count);
            Assert.Equal("Eggbert", egg.Get<NbtString>("name").Value);
            Assert.Equal(0.5, egg.Get<NbtFloat>("value").Value);
            #endregion nested compounds

            #region lists
            var listLongTest = main.Get<NbtList>("listTest (long)");
            Assert.Equal(5, listLongTest.Count);

            var count = 11;

            foreach (var item in listLongTest)
                Assert.Equal(count++, item.LongValue);


            var listCompoundTest = main.Get<NbtList>("listTest (compound)");
            Assert.Equal(2, listCompoundTest.Count);

            var compound1 = listCompoundTest[0] as NbtCompound;
            Assert.Equal("Compound tag #0", compound1.Get<NbtString>("name").Value);
            Assert.Equal(1264099775885, compound1.Get<NbtLong>("created-on").Value);


            var compound2 = listCompoundTest[1] as NbtCompound;
            Assert.Equal("Compound tag #1", compound2.Get<NbtString>("name").Value);
            Assert.Equal(1264099775885, compound2.Get<NbtLong>("created-on").Value);
            #endregion lists
        }

        [Fact]
        public async Task ReadSlot()
        {
            await using var stream = new MinecraftStream();

            var itemMeta = new ItemMetaBuilder()
                .WithName("test")
                .WithDurability(1)
                .Build();

            /*var dataSlot = new ItemStack(25, 0, itemMeta)
            {
                Present = true
            };

            await stream.WriteSlotAsync(dataSlot);

            stream.Position = 0;

            var slot = await stream.ReadSlotAsync();

            Assert.True(slot.Present);
            Assert.Equal(0, slot.Count);
            Assert.Equal(25, slot.Id);

            Assert.Equal("test", slot.ItemMeta.Value.Name);
            Assert.Equal(1, slot.ItemMeta.Value.Durability);*/
        }
    }
}
