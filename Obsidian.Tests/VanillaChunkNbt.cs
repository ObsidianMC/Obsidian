using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Registries;
using Obsidian.Commands.Modules;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Pistons;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Placement;
using Obsidian.WorldData.Upgrade;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class VanillaChunkNbt
{
    [Fact]
    public async Task CapturedChunkAndEntitiesRoundTrip()
    {
        var fixture = FixtureDimension();
        var captured = await ReadRegion(fixture, "region");
        var capturedEntities = await ReadRegion(fixture, "entities");
        var destination = Path.Join(Path.GetTempPath(), $"obsidian-chunk26-{Guid.NewGuid():N}");
        try
        {
            IChunk chunk;
            await using (var region = new Region(0, 0, fixture, "region"))
            {
                await region.InitAsync();
                chunk = await region.GetChunkAsync(0, 0);
                Assert.Equal(BlocksRegistry.Stone, chunk.GetBlock(10, -60, 0));
                var stairs = chunk.GetBlock(8, -60, 0);
                Assert.Equal("minecraft:oak_stairs", stairs.UnlocalizedName);
                Assert.Equal("east", stairs.GetProperty("facing"));
                Assert.Equal("top", stairs.GetProperty("half"));
                Assert.Equal("foot", chunk.GetBlock(12, -60, 0).GetProperty("part"));
                Assert.Equal("head", chunk.GetBlock(12, -60, 1).GetProperty("part"));
                Assert.Null(chunk.GetBlockEntity(12, -60, 0));
                Assert.Null(chunk.GetBlockEntity(12, -60, 1));

                foreach (var original in ((NbtList)captured["block_entities"]).Cast<NbtCompound>())
                {
                    var entity = chunk.GetBlockEntity(original.GetInt("x"), original.GetInt("y"), original.GetInt("z"));
                    Assert.NotNull(entity);
                    AssertNbt(original, Read(Write(BlockEntityNbt.Save(entity!)!)));
                }

                var furnace = Assert.IsType<FurnaceBlockEntity>(chunk.GetBlockEntity(0, -60, 0));
                Assert.Equal(capturedBlockEntity("minecraft:furnace").GetInt("lit_total_time"), furnace.LitTotal);
                Assert.Equal(2, furnace.Inventory.GetItem(0)!.Count);
                var brewing = Assert.IsType<BrewingStandBlockEntity>(chunk.GetBlockEntity(2, -60, 0));
                Assert.Equal(20, brewing.Stand.Fuel);
                var pot = Assert.IsType<DataBlockEntity>(chunk.GetBlockEntity(4, -60, 0)).SnapshotData();
                Assert.Equal("minecraft:angler_pottery_sherd", ((NbtCompound)((NbtCompound)pot["sherds"])["back"]).GetString("id"));
                Assert.Equal(5, ((NbtCompound)pot["item"]).GetInt("count"));
                var sign = Assert.IsType<DataBlockEntity>(chunk.GetBlockEntity(6, -60, 0)).SnapshotData();
                Assert.True(sign.GetBool("is_waxed"));
                Assert.Equal("Hello", ((NbtTag<string>)((NbtList)((NbtCompound)sign["front_text"])["messages"])[0]).Value);
                var chest = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(14, -60, 0));
                Assert.Same(ItemsRegistry.Diamond, chest.Inventory.GetItem(0)!.Holder);
                Assert.Equal(3, chest.Inventory.GetItem(0)!.Count);

                var armorStand = Assert.Single(((Chunk)chunk).PendingEntities);
                Assert.Equal("minecraft:armor_stand", armorStand.Type);
                AssertNbt(((NbtList)capturedEntities["Entities"])[0], EntityNbt.ToNbt(armorStand));
                // Preserve a tag no Obsidian model knows, through the real writer and reader.
                ((Chunk)chunk).UnmodeledTags.Add(new NbtCompound("fixture_extension") { new NbtTag<int>("value", 42) });
            }

            await using (var saved = new Region(0, 0, destination, "region"))
            {
                await saved.InitAsync();
                saved.SetChunk(chunk);
                await saved.FlushAsync();
            }
            var actual = await ReadRegion(destination, "region");
            foreach (var key in new[] { "DataVersion", "Status", "xPos", "zPos", "yPos", "LastUpdate", "InhabitedTime", "structures", "PostProcessing", "block_ticks", "fluid_ticks", "Heightmaps" })
                AssertNbt(captured[key], actual[key], allowExtraFields: key == "Heightmaps");
            Assert.Equal(42, ((NbtCompound)actual["fixture_extension"]).GetInt("value"));
            // Obsidian deliberately asks vanilla to recalculate its lighting.
            Assert.False(actual.GetBool("isLightOn"));
            var sections = ((NbtList)actual["sections"]).Cast<NbtCompound>().ToDictionary(section => section.GetByte("Y"));
            foreach (var section in ((NbtList)captured["sections"]).Cast<NbtCompound>().Where(section => section.HasTag("block_states")))
            {
                var saved = sections[section.GetByte("Y")];
                AssertNbt(section["block_states"], saved["block_states"]);
                AssertNbt(section["biomes"], saved["biomes"]);
                foreach (var light in new[] { "BlockLight", "SkyLight" })
                    if (section.TryGetTag(light, out var capturedLight))
                        AssertNbt(capturedLight, saved[light]);
            }
            AssertNbt(captured["block_entities"], actual["block_entities"], unorderedList: true);
            AssertNbt(capturedEntities, await ReadRegion(destination, "entities"));

            NbtCompound capturedBlockEntity(string id) => ((NbtList)captured["block_entities"]).Cast<NbtCompound>().Single(entity => entity.GetString("id") == id);
        }
        finally
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
        }
    }

    [Theory]
    [InlineData("noise")]
    [InlineData("minecraft:surface")]
    [InlineData("minecraft:carvers")]
    [InlineData("minecraft:terrain")]
    public void LegacyNbtUpgradesWithoutLosingState(string status)
    {
        var pistonState = BlockStateProperties.GetState("minecraft:oak_stairs").WithProperty("facing", "east").WithProperty("half", "top");
        var palette = new NbtList(NbtTagType.Compound, "palette")
        {
            LegacyState("", "minecraft:furnace"),
            LegacyState("", "minecraft:oak_stairs", ("facing", "east"), ("half", "top"))
        };
        var entities = new NbtList(NbtTagType.Compound, "block_entities")
        {
            new NbtCompound { new NbtTag<string>("id", "minecraft:furnace"), new NbtTag<short>("cooking_time_spent", 5), new NbtTag<short>("cooking_total_time", 200), new NbtTag<short>("lit_time_remaining", 100), new NbtTag<short>("lit_total_time", 300) },
            new NbtCompound { new NbtTag<string>("id", "minecraft:brewing_stand"), new NbtTag<short>("BrewTime", 123), new NbtTag<byte>("Fuel", 19) },
            new NbtCompound { new NbtTag<string>("id", "minecraft:decorated_pot"), new NbtList(NbtTagType.String, "sherds") { new NbtTag<string>("", "minecraft:angler_pottery_sherd"), new NbtTag<string>("", "minecraft:brick"), new NbtTag<string>("", "minecraft:brick"), new NbtTag<string>("", "minecraft:arms_up_pottery_sherd") }, new NbtCompound("item") { new NbtTag<string>("id", "minecraft:emerald"), new NbtTag<int>("count", 5) } },
            new NbtCompound { new NbtTag<string>("id", "minecraft:bed") },
            new NbtCompound { new NbtTag<string>("id", "minecraft:piston"), new NbtTag<int>("MovedState", LegacyWorldgenBlockStates.StateId(pistonState)), new NbtTag<string>("Facing", "east"), new NbtTag<byte>("Extending", 1), new NbtTag<byte>("Source", 0), new NbtTag<float>("Progress", 0.5f) }
        };
        var chunk = new NbtCompound
        {
            new NbtTag<int>("DataVersion", 4671), new NbtTag<string>("Status", status),
            new NbtList(NbtTagType.Compound, "sections") { new NbtCompound { new NbtCompound("block_states") { palette } } },
            entities, new NbtCompound("untouched") { new NbtTag<int>("value", 77) }
        };
        LegacyChunkUpgrade.UpgradeChunk(chunk);
        Assert.Equal(ServerConstants.DataVersion, chunk.GetInt("DataVersion"));
        Assert.Equal("minecraft:terrain", chunk.GetString("Status"));
        Assert.Equal(ChunkGenStage.carvers, Region.ParseStatus(chunk.GetString("Status")));
        Assert.Equal(77, ((NbtCompound)chunk["untouched"]).GetInt("value"));
        var upgradedPalette = (NbtList)((NbtCompound)((NbtCompound)((NbtList)chunk["sections"])[0])["block_states"])["palette"];
        Assert.Equal("minecraft:furnace", ((NbtTag<string>)((NbtCompound)upgradedPalette[0])[""]).Value);
        Assert.Equal(pistonState, BlockStateNbt.Read(upgradedPalette[1]));
        var upgradedStairs = Assert.IsType<NbtCompound>(upgradedPalette[1]);
        Assert.Equal("minecraft:oak_stairs", upgradedStairs.GetString("id"));
        Assert.False(upgradedStairs.HasTag("Name"));
        Assert.Equal("top", ((NbtCompound)upgradedStairs["properties"]).GetString("half"));
        Assert.Equal(4, entities.Count);
        var furnace = (NbtCompound)entities[0];
        Assert.Equal(5, furnace.GetInt("cooking_time_spent"));
        Assert.Equal(200, furnace.GetInt("cooking_total_time"));
        Assert.Equal(100, furnace.GetInt("lit_time_remaining"));
        Assert.Equal(300, furnace.GetInt("lit_total_time"));
        Assert.Equal(1f, furnace.GetFloat("speed_multiplier"));
        var stand = (NbtCompound)entities[1];
        Assert.Equal(123, stand.GetInt("BrewTime"));
        Assert.Equal(19, stand.GetInt("Fuel"));
        Assert.Equal(400, stand.GetInt("total_brew_time"));
        Assert.Equal(20, stand.GetInt("total_fuel"));
        Assert.Equal(1f, stand.GetFloat("speed_multiplier"));
        var pot = (NbtCompound)entities[2];
        var sherds = (NbtCompound)pot["sherds"];
        Assert.Equal("minecraft:angler_pottery_sherd", ((NbtCompound)sherds["back"]).GetString("id"));
        Assert.Equal("minecraft:brick", ((NbtCompound)sherds["left"]).GetString("id"));
        Assert.Equal("minecraft:brick", ((NbtCompound)sherds["right"]).GetString("id"));
        Assert.Equal("minecraft:arms_up_pottery_sherd", ((NbtCompound)sherds["front"]).GetString("id"));
        Assert.Equal(5, ((NbtCompound)pot["item"]).GetInt("count"));
        var piston = (NbtCompound)entities[3];
        Assert.Equal(pistonState, BlockStateNbt.Read(piston["blockState"]));
        Assert.Equal((int)BlockFace.East, piston.GetInt("facing"));
        Assert.True(piston.GetBool("extending"));
        Assert.False(piston.GetBool("source"));
        Assert.Equal(0.5f, piston.GetFloat("progress"));
        var moving = new MovingPistonBlockEntity();
        moving.Load(piston);
        Assert.Equal(pistonState, moving.MovedState);
        var savedPiston = new NbtCompound();
        moving.Save(savedPiston);
        Assert.Equal(pistonState, BlockStateNbt.Read(savedPiston["blockState"]));
        Assert.Equal(0.5f, savedPiston.GetFloat("progress"));
        var once = Write(chunk);
        Assert.Equal(once, Write(LegacyChunkUpgrade.UpgradeChunk(chunk)));

        var entityChunk = new NbtCompound { new NbtList(NbtTagType.Compound, "Entities")
        {
            new NbtCompound { new NbtTag<string>("id", "minecraft:enderman"), LegacyState("carriedBlockState", "minecraft:stone"), new NbtList(NbtTagType.Compound, "Passengers") { new NbtCompound { LegacyState("DisplayState", "minecraft:oak_stairs", ("facing", "east"), ("half", "top")) } } }
        } };
        LegacyChunkUpgrade.UpgradeEntityChunk(entityChunk);
        var enderman = (NbtCompound)((NbtList)entityChunk["Entities"])[0];
        Assert.Equal("minecraft:stone", enderman.GetString("carriedBlockState"));
        Assert.Equal(pistonState, BlockStateNbt.Read(((NbtCompound)((NbtList)enderman["Passengers"])[0])["DisplayState"]));
        once = Write(entityChunk);
        Assert.Equal(once, Write(LegacyChunkUpgrade.UpgradeEntityChunk(entityChunk)));

        var template = Template(new NbtList(NbtTagType.Compound, "palette") { LegacyState("", "minecraft:furnace") });
        LegacyChunkUpgrade.UpgradeStructureTemplate(template);
        Assert.Equal("minecraft:furnace", ((NbtTag<string>)((NbtList)template["palette"])[0]).Value);
        Assert.Equal(ServerConstants.DataVersion, template.GetInt("DataVersion"));
        once = Write(template);
        Assert.Equal(once, Write(LegacyChunkUpgrade.UpgradeStructureTemplate(template)));

        var staged = new Chunk(0, 0);
        staged.SetChunkStatus(Region.ParseStatus(status));
        using var bytes = new MemoryStream();
        using (var writer = new NbtWriterStream(bytes, NbtCompression.None, ""))
        {
            Region.SerializeChunk(writer, staged, null);
            writer.EndCompound();
            writer.TryFinish();
        }
        Assert.Equal("minecraft:terrain", Read(bytes.ToArray()).GetString("Status"));
        Assert.Null(BlockEntityNbt.Save(new DataBlockEntity { Id = "minecraft:bed", BlockPosition = Vector.Zero }));
    }

    [Fact(DisplayName = "Legacy palette entries with a numeric state id keep that state's properties")]
    public void LegacyPaletteIdsKeepTheirState()
    {
        var stairs = BlocksRegistry.OakStairs.WithProperty("facing", "east").WithProperty("half", "top");
        var furnace = BlockStateProperties.GetState("minecraft:furnace").WithProperty("lit", "true");
        var palette = new NbtList(NbtTagType.Compound, "palette")
        {
            // The name without properties, as Obsidian saved it beside the id.
            WithId(LegacyState("", "minecraft:oak_stairs"), LegacyWorldgenBlockStates.StateId(stairs)),
            WithId(new NbtCompound(), LegacyWorldgenBlockStates.StateId(furnace)),
            // An id past 1.21.11's table falls back to the name.
            WithId(LegacyState("", "minecraft:granite"), int.MaxValue)
        };
        var chunk = new NbtCompound
        {
            new NbtList(NbtTagType.Compound, "sections") { new NbtCompound { new NbtCompound("block_states") { palette } } }
        };

        LegacyChunkUpgrade.UpgradeChunk(chunk);

        var upgraded = (NbtList)((NbtCompound)((NbtCompound)((NbtList)chunk["sections"])[0])["block_states"])["palette"];
        Assert.Equal([stairs, furnace, BlockStateProperties.GetState("minecraft:granite")], upgraded.Select(BlockStateNbt.Read));
        Assert.All(upgraded.OfType<NbtCompound>(), entry => Assert.False(entry.HasTag("Id")));

        static NbtCompound WithId(NbtCompound state, int id)
        {
            state.Add(new NbtTag<int>("Id", id));
            return state;
        }
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("mixed")]
    [InlineData("bare")]
    public void TemplateReadersAcceptBothVersions(string shape)
    {
        var block = shape == "bare" ? BlockStateProperties.GetState("minecraft:furnace") : BlocksRegistry.OakStairs.WithProperty("facing", "east").WithProperty("half", "top");
        var palette = new NbtList(shape == "bare" ? NbtTagType.String : NbtTagType.Compound, "palette");
        palette.Add(shape == "legacy" ? LegacyState("", block.UnlocalizedName, ("facing", "east"), ("half", "top"))
            : shape == "bare" ? BlockStateNbt.Write(block) : BlockStateNbt.WrapPaletteEntry(BlockStateNbt.Write(block)));
        if (shape == "mixed")
            palette.Add(BlockStateNbt.WrapPaletteEntry(BlockStateNbt.Write(BlocksRegistry.Furnace)));
        var root = Template(palette);
        foreach (var compression in new[] { NbtCompression.None, NbtCompression.GZip })
        {
            using var data = new MemoryStream(Write(root, compression));
            var loaded = StructureTemplate.Load(data, compression);
            var info = Assert.Single(loaded.FilterBlocks(Vector.Zero, new StructurePlaceSettings(), block.RegistryId));
            Assert.Equal(block, info.Block);
        }
        Assert.Equal(block, Assert.Single(SchematicCommandModule.Read(Write(root)).Blocks).Block);
    }

    [Fact]
    public void EntityHeldStatesSurviveSaving()
    {
        foreach (var block in new[] { BlockStateProperties.GetState("minecraft:furnace"), BlocksRegistry.OakStairs.WithProperty("facing", "east").WithProperty("half", "top") })
        {
            var enderman = new Enderman { Level = null!, CarriedBlock = block };
            using var writer = new RawNbtWriter("");
            enderman.WriteSave(writer, writeCompound: false);
            writer.EndCompound();
            writer.TryFinish();
            var tag = Read(writer.Data.ToArray());
            Assert.Equal(block, BlockStateNbt.Read(tag["carriedBlockState"]));
            var restored = new Enderman { Level = null! };
            restored.ReadSave(tag);
            Assert.Equal(block, restored.CarriedBlock);

            var minecart = new Minecart { Level = null!, DisplayBlock = block };
            tag = new NbtCompound();
            minecart.WriteNbt(tag);
            Assert.Equal(block, BlockStateNbt.Read(tag["DisplayState"]));
            var loadedCart = new Minecart { Level = null! };
            loadedCart.ReadNbt(tag);
            Assert.Equal(block, loadedCart.DisplayBlock);
            if (block.UnlocalizedName == "minecraft:furnace")
                Assert.IsType<NbtTag<string>>(tag["DisplayState"]);
            else
                Assert.IsType<NbtCompound>(tag["DisplayState"]);
        }
    }

    private static NbtCompound LegacyState(string key, string id, params (string Key, string Value)[] properties)
    {
        var state = new NbtCompound(key) { new NbtTag<string>("Name", id) };
        if (properties.Length > 0)
        {
            var values = new NbtCompound("Properties");
            foreach (var (name, value) in properties)
                values.Add(new NbtTag<string>(name, value));
            state.Add(values);
        }
        return state;
    }

    private static NbtCompound Template(NbtList palette) => new()
    {
        new NbtList(NbtTagType.Int, "size") { new NbtTag<int>("", 1), new NbtTag<int>("", 1), new NbtTag<int>("", 1) },
        palette,
        new NbtList(NbtTagType.Compound, "blocks") { new NbtCompound { new NbtTag<int>("state", 0), new NbtList(NbtTagType.Int, "pos") { new NbtTag<int>("", 0), new NbtTag<int>("", 0), new NbtTag<int>("", 0) } } }
    };

    private static string FixtureDimension()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Join(directory.FullName, "Obsidian.Tests", "Assets", "vanilla-26.3-world", "dimensions", "minecraft", "overworld");
            if (Directory.Exists(path))
                return path;
        }
        throw new DirectoryNotFoundException("Cannot find the vanilla 26.3 fixture in the source tree.");
    }

    private static async Task<NbtCompound> ReadRegion(string folder, string kind)
    {
        await using var file = new RegionFile(Path.Join(folder, kind, "r.0.0.mca"), NbtCompression.ZLib);
        await file.InitializeAsync();
        var bytes = (await file.GetChunkBytesAsync(0, 0))!.Value;
        return Read(bytes.ToArray());
    }

    private static NbtCompound Read(byte[] bytes)
    {
        var reader = new NbtReader(new MemoryStream(bytes));
        return Assert.IsType<NbtCompound>(reader.ReadNextTag());
    }

    private static byte[] Write(NbtCompound tag, NbtCompression compression = NbtCompression.None)
    {
        using var stream = new MemoryStream();
        using (var writer = new NbtWriterStream(stream, compression, ""))
        {
            writer.WriteEntries(tag);
            writer.EndCompound();
            writer.TryFinish();
        }
        return stream.ToArray();
    }

    private static void AssertNbt(INbtTag expected, INbtTag actual, bool allowExtraFields = false, bool unorderedList = false)
    {
        Assert.Equal(expected.Type, actual.Type);
        if (expected is NbtCompound compound)
        {
            var other = Assert.IsType<NbtCompound>(actual);
            if (!allowExtraFields)
                Assert.Equal(compound.Select(entry => entry.Key).Order(), other.Select(entry => entry.Key).Order());
            foreach (var (key, value) in compound)
                AssertNbt(value, other[key]);
        }
        else if (expected is NbtList list)
        {
            var other = Assert.IsType<NbtList>(actual);
            Assert.Equal(list.Count, other.Count);
            // Empty lists have no payload; vanilla writes End while Obsidian retains the declared element type.
            if (list.Count > 0)
                Assert.Equal(list.ListType, other.ListType);
            var entries = unorderedList ? list.Cast<NbtCompound>().OrderBy(tag => tag.GetInt("x")).Cast<INbtTag>().ToList() : list.ToList();
            var actualEntries = unorderedList ? other.Cast<NbtCompound>().OrderBy(tag => tag.GetInt("x")).Cast<INbtTag>().ToList() : other.ToList();
            for (var i = 0; i < entries.Count; i++)
                AssertNbt(entries[i], actualEntries[i]);
        }
        else if (expected is NbtArray<long> longs)
            Assert.Equal(longs.GetArray(), Assert.IsType<NbtArray<long>>(actual).GetArray());
        else if (expected is NbtArray<int> ints)
            Assert.Equal(ints.GetArray(), Assert.IsType<NbtArray<int>>(actual).GetArray());
        else if (expected is NbtArray<byte> bytes)
            Assert.Equal(bytes.GetArray(), Assert.IsType<NbtArray<byte>>(actual).GetArray());
        else
            Assert.Equal(expected.ToString(), actual.ToString());
    }
}
