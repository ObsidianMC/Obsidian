using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Dimension = Obsidian.WorldData.Dimension;
using PlayerEntity = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Chunks and players in the shapes vanilla 1.21.11 saves them, built here from those shapes (not captured saves).
/// </summary>
public sealed class VanillaSaves : IDisposable
{
    private static readonly Dictionary<string, string> stairsProperties = new()
    {
        ["facing"] = "east",
        ["half"] = "top",
        ["shape"] = "straight",
        ["waterlogged"] = "false"
    };

    private static readonly Guid playerUuid = Guid.Parse("0f3c2b1a-1111-2222-3333-444455556666");

    // Where the synthetic chunk has its stairs and desert, in section 0 of chunk (0, 0).
    private static readonly Vector stairs = new(3, 5, 7);
    private static readonly Vector desert = new(4, 0, 8);

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-vanilla-saves-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.folder))
            Directory.Delete(this.folder, recursive: true);
    }

    [Fact(DisplayName = "A vanilla chunk loads with its block states by name, biomes and namespaced status")]
    public async Task VanillaChunkLoads()
    {
        await WriteChunkAsync(this.folder, VanillaChunk());

        var chunk = await LoadChunkAsync(this.folder);

        Assert.Equal(ChunkGenStage.full, chunk.ChunkStatus);
        Assert.Equal(BlockStateProperties.GetState("minecraft:oak_stairs", stairsProperties), chunk.GetBlock(stairs));
        Assert.Equal("east", chunk.GetBlock(stairs).GetProperty("facing"));
        Assert.Equal(BlocksRegistry.Get(Material.Stone), chunk.GetBlock(stairs + new Vector(1, 0, 0)));
        Assert.Equal(BlocksRegistry.Get(Material.Granite), chunk.GetBlock(0, 16, 0));
        Assert.Equal("minecraft:desert", chunk.GetBiome(desert.X, desert.Y, desert.Z).Name);
        Assert.Equal("minecraft:plains", chunk.GetBiome(0, 0, 0).Name);
    }

    [Fact(DisplayName = "Obsidian saves chunks in vanilla's section shape and reloads them unchanged")]
    public async Task SavedChunkHasVanillaShape()
    {
        await WriteChunkAsync(this.folder, VanillaChunk());
        var loaded = await LoadChunkAsync(this.folder);

        // Post-processing marks in two sections.
        var marks = ((Chunk)loaded).PostProcessing;
        marks.Add(new Vector(3, -60, 5));
        marks.Add(new Vector(15, 17, 0));

        // Saved again by Obsidian, into another world.
        var savedFolder = Path.Join(this.folder, "saved");
        await using (var region = new Region(0, 0, savedFolder, "region"))
        {
            await region.InitAsync();
            region.SetChunk(loaded);
            await region.FlushAsync();
        }

        var saved = await ReadChunkAsync(savedFolder);
        Assert.Equal(4671, saved.GetInt("DataVersion"));
        Assert.Equal("minecraft:full", saved.GetString("Status"));

        var section = ((NbtList)saved["sections"]).Cast<NbtCompound>().Single(compound => compound.GetByte("Y") == 0);
        var blockStates = (NbtCompound)section["block_states"];
        var palette = ((NbtList)blockStates["palette"]).Cast<NbtCompound>().ToList();
        Assert.All(palette, entry => Assert.False(entry.HasTag("Id")));

        var savedStairs = palette.Single(entry => entry.GetString("Name") == "minecraft:oak_stairs");
        Assert.Equal("top", ((NbtCompound)savedStairs["Properties"]).GetString("half"));

        // Two states pack 4 bits per block (16 per long), as vanilla requires.
        Assert.Equal(256, ((NbtArray<long>)blockStates["data"]).Count);
        Assert.Equal(new[] { "minecraft:plains", "minecraft:desert" },
            ((NbtList)((NbtCompound)section["biomes"])["palette"]).Cast<NbtTag<string>>().Select(biome => biome.Value));

        var reloaded = await LoadChunkAsync(savedFolder);
        for (var y = -64; y < 32; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    Assert.Equal(loaded.GetBlock(x, y, z), reloaded.GetBlock(x, y, z));
            }
        }

        Assert.Equal("minecraft:desert", reloaded.GetBiome(desert.X, desert.Y, desert.Z).Name);

        // Vanilla's post-processing: a list of packed positions for every section, x | y << 4 | z << 8 within it.
        var postProcessing = ((NbtList)saved["PostProcessing"]).Cast<NbtList>().ToList();
        Assert.Equal(loaded.Sections.Length, postProcessing.Count);
        Assert.Equal([(short)(3 | 4 << 4 | 5 << 8)], postProcessing[0].Cast<NbtTag<short>>().Select(mark => mark.Value));
        Assert.Equal([(short)(15 | 1 << 4)], postProcessing[5].Cast<NbtTag<short>>().Select(mark => mark.Value));
        Assert.Equal(marks, ((Chunk)reloaded).PostProcessing);

        var blockTick = ((NbtList)saved["block_ticks"]).Cast<NbtCompound>().Single();
        Assert.Equal("minecraft:repeater", blockTick.GetString("i"));
        Assert.Equal(2, blockTick.GetInt("t"));

        var references = (NbtCompound)((NbtCompound)saved["structures"])["References"];
        Assert.Equal([1L], ((NbtArray<long>)references["minecraft:village_plains"]).GetArray());
    }

    [Fact(DisplayName = "A vanilla player loads, and saves keep vanilla's names and what Obsidian doesn't model")]
    public async Task VanillaPlayerRoundTrips()
    {
        var world = this.CreateWorld();
        var player = CreatePlayer(world, owner: false);
        await PlayerDataFile.WriteAsync(world.GetPlayerDataPath(player.Uuid), VanillaPlayer(xpLevel: 5));

        try
        {
            await player.LoadAsync(loadFromPersistentWorld: false);

            Assert.Equal(new VectorD(1.5, 70, -2.5), player.Position);
            Assert.Equal(GameMode.Creative, player.GameMode);
            Assert.Equal(15f, player.Health);
            Assert.Equal(17, player.FoodLevel);
            Assert.Equal(0.25f, player.XpP);
            Assert.Equal(playerUuid, player.Uuid);
            Assert.Equal(39, player.CurrentHeldItemSlot);
            Assert.Equal(5, player.Inventory.GetItem(39)!.Damage);
            Assert.Equal(64, player.Inventory.GetItem(9)!.Count);
            Assert.Equal(Material.IronHelmet, player.Inventory.GetItem(5)!.Type);
            Assert.Equal(Material.Shield, player.Inventory.GetItem(45)!.Type);
            Assert.Equal(3, player.EnderInventory.GetItem(0)!.Count);

            player.XpLevel = 6;
            await player.SaveAsync();

            var saved = PlayerDataFile.Read(world.GetPlayerDataPath(player.Uuid), NullLogger.Instance)!;
            Assert.Equal(6, saved.GetInt("XpLevel"));
            Assert.Equal(3, saved.GetInt("SelectedItemSlot"));
            Assert.Equal("minecraft:overworld", saved.GetString("Dimension"));
            Assert.Equal(1.0, saved.GetDouble("fall_distance"));
            Assert.True(((NbtCompound)saved["recipeBook"]).GetBool("isGuiOpen"));
            Assert.Equal("minecraft:iron_helmet", ((NbtCompound)((NbtCompound)saved["equipment"])["head"]).GetString("id"));

            var sword = ((NbtList)saved["Inventory"]).Cast<NbtCompound>().Single(item => item.GetByte("Slot") == 3);
            Assert.Equal(1, sword.GetInt("count"));
            Assert.False(sword.HasTag("Count"));

            var components = (NbtCompound)sword["components"];
            Assert.Equal(5, components.GetInt("minecraft:damage"));
            Assert.Equal("dev", ((NbtCompound)components["minecraft:custom_data"]).GetString("owner"));
        }
        finally
        {
            DeletePersistentData(player);
        }
    }

    [Fact(DisplayName = "The singleplayer owner loads from level.dat's Data.Player and saves to it and playerdata")]
    public async Task OwnerUsesLevelData()
    {
        var world = this.CreateWorld();
        var player = CreatePlayer(world, owner: true);

        await PlayerDataFile.WriteAsync(world.GetPlayerDataPath(player.Uuid), VanillaPlayer(xpLevel: 1));
        world.SingleplayerPlayerData = VanillaPlayer(xpLevel: 30);

        try
        {
            await player.LoadAsync(loadFromPersistentWorld: false);
            Assert.Equal(30, player.XpLevel);

            player.XpLevel = 31;
            await player.SaveAsync();
            await world.SaveAsync();

            var playerData = PlayerDataFile.Read(world.GetPlayerDataPath(player.Uuid), NullLogger.Instance)!;
            Assert.Equal(31, playerData.GetInt("XpLevel"));

            var levelData = World.ReadLevelData(Path.Join(this.folder, "level.dat"), NullLogger.Instance)!;
            var owner = (NbtCompound)((NbtCompound)levelData["Data"])["Player"];
            Assert.Equal(31, owner.GetInt("XpLevel"));
        }
        finally
        {
            DeletePersistentData(player);
        }
    }

    [Fact(DisplayName = "Concurrent saves of a player all succeed and leave a readable file and its backup")]
    public async Task ConcurrentSavesSucceed()
    {
        var world = this.CreateWorld();
        var player = CreatePlayer(world, owner: false);

        try
        {
            await player.LoadAsync(loadFromPersistentWorld: false);
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(player.SaveAsync)));

            var path = world.GetPlayerDataPath(player.Uuid);
            Assert.NotNull(PlayerDataFile.Read(path, NullLogger.Instance));
            Assert.True(File.Exists(PlayerDataFile.BackupPath(path)));
            Assert.Empty(Directory.GetFiles(world.PlayerDataPath, "*.tmp"));
        }
        finally
        {
            DeletePersistentData(player);
        }
    }

    [Fact(DisplayName = "Saved items apply removed defaults, and differ from items with other unmodeled components")]
    public void SavedItemComponents()
    {
        var pickaxe = new NbtCompound
        {
            new NbtTag<string>("id", "minecraft:diamond_pickaxe"),
            new NbtTag<int>("count", 1),
            new NbtCompound("components") { new NbtCompound("!minecraft:max_damage") }
        };

        var loaded = pickaxe.ItemFromNbt()!;
        Assert.Equal(0, loaded.MaxDamage);
        Assert.Contains(DataComponentType.MaxDamage, loaded.RemoveComponents);
        Assert.True(((NbtCompound)loaded.ToNbt()["components"]).HasTag("!minecraft:max_damage"));

        // The explosion is a component Obsidian doesn't model, so only the saved NBT tells the stars apart.
        var red = FireworkStar(0xFF0000).ItemFromNbt()!;
        Assert.Equal(red, FireworkStar(0xFF0000).ItemFromNbt());
        Assert.NotEqual(red, FireworkStar(0x0000FF).ItemFromNbt());
    }

    [Fact(DisplayName = "A new world reads its game rules, and its dimensions share its difficulty")]
    public void NewWorldLevelData()
    {
        var world = this.CreateWorld(new NbtCompound("Data")
        {
            new NbtTag<string>("LevelName", "world"),
            new NbtTag<byte>("Difficulty", (byte)Difficulty.Peaceful),
            new NbtCompound("game_rules") { new NbtTag<int>("minecraft:random_tick_speed", 0) }
        });

        Assert.Equal(0, world.LevelData.GetIntegerRule("random_tick_speed"));

        CodecRegistry.TryGetDimension("minecraft:the_nether", out var netherCodec);
        var nether = new Dimension(NullLogger<Dimension>.Instance, null!, new FixedOptions(new ServerConfiguration()),
            null!, new EmptyWorldGenerator(), netherCodec!.Name, world);
        nether.Initialize(netherCodec);
        world.RegisterDimension(netherCodec, nether);
        Assert.Equal(Difficulty.Peaceful, nether.LevelData.Difficulty);

        world.SetDifficulty(Difficulty.Hard, locked: true);
        Assert.Equal(Difficulty.Hard, nether.LevelData.Difficulty);
        Assert.True(nether.LevelData.DifficultyLocked);
    }

    private static NbtCompound FireworkStar(int color) => new()
    {
        new NbtTag<string>("id", "minecraft:firework_star"),
        new NbtTag<int>("count", 1),
        new NbtCompound("components")
        {
            new NbtCompound("minecraft:firework_explosion")
            {
                new NbtTag<string>("shape", "small_ball"),
                new NbtArray<int>("colors", [color])
            }
        }
    };

    private World CreateWorld(NbtCompound? data = null)
    {
        var world = new World(NullLogger<World>.Instance, null!, null!, new FixedOptions(new ServerConfiguration()), null!,
            new EmptyWorldGenerator(), "world", "0");

        world.UseVanillaLayout(this.folder);
        CodecRegistry.TryGetDimension("minecraft:overworld", out var overworld);
        world.Initialize(overworld!);
        world.CreateVanillaLevel(data ?? new NbtCompound("Data") { new NbtTag<string>("LevelName", "world") });

        return world;
    }

    private static PlayerEntity CreatePlayer(World world, bool owner) => new(playerUuid, "dev", new FakeClient(), world)
    {
        Server = new FakeServer(),
        IsSingleplayerOwner = owner
    };

    private static void DeletePersistentData(PlayerEntity player)
    {
        File.Delete(player.PersistentDataFile);
        File.Delete(PlayerDataFile.BackupPath(player.PersistentDataFile));
    }

    /// <summary>
    /// A player as vanilla 1.21.11 saves them: an item with a component Obsidian doesn't model, equipment, an ender chest,
    /// and fields Obsidian doesn't model at all (the recipe book, and another player's UUID).
    /// </summary>
    private static NbtCompound VanillaPlayer(int xpLevel) => new()
    {
        new NbtTag<int>("DataVersion", 4671),
        new NbtList(NbtTagType.Double, "Pos")
        {
            new NbtTag<double>(string.Empty, 1.5),
            new NbtTag<double>(string.Empty, 70),
            new NbtTag<double>(string.Empty, -2.5)
        },
        new NbtList(NbtTagType.Float, "Rotation") { new NbtTag<float>(string.Empty, 90f), new NbtTag<float>(string.Empty, 10f) },
        new NbtArray<int>("UUID", [1, 2, 3, 4]),
        new NbtTag<string>("Dimension", "minecraft:overworld"),
        new NbtTag<int>("playerGameType", 1),
        new NbtTag<float>("Health", 15f),
        new NbtTag<int>("foodLevel", 17),
        new NbtTag<int>("XpLevel", xpLevel),
        new NbtTag<float>("XpP", 0.25f),
        new NbtTag<double>("fall_distance", 1.0),
        new NbtTag<int>("SelectedItemSlot", 3),
        new NbtList(NbtTagType.Compound, "Inventory")
        {
            new NbtCompound
            {
                new NbtTag<byte>("Slot", 3),
                new NbtTag<string>("id", "minecraft:diamond_sword"),
                new NbtTag<int>("count", 1),
                new NbtCompound("components")
                {
                    new NbtTag<int>("minecraft:damage", 5),
                    new NbtCompound("minecraft:custom_data") { new NbtTag<string>("owner", "dev") }
                }
            },
            new NbtCompound
            {
                new NbtTag<byte>("Slot", 9),
                new NbtTag<string>("id", "minecraft:stone"),
                new NbtTag<int>("count", 64)
            }
        },
        new NbtCompound("equipment")
        {
            new NbtCompound("head") { new NbtTag<string>("id", "minecraft:iron_helmet"), new NbtTag<int>("count", 1) },
            new NbtCompound("offhand") { new NbtTag<string>("id", "minecraft:shield"), new NbtTag<int>("count", 1) }
        },
        new NbtList(NbtTagType.Compound, "EnderItems")
        {
            new NbtCompound
            {
                new NbtTag<byte>("Slot", 0),
                new NbtTag<string>("id", "minecraft:apple"),
                new NbtTag<int>("count", 3)
            }
        },
        new NbtCompound("recipeBook") { new NbtTag<byte>("isGuiOpen", 1) }
    };

    /// <summary>
    /// A complete chunk as vanilla 1.21.11 saves it, with two sections: section 0 holds stone with oak stairs (by name and
    /// properties) at <see cref="stairs"/> over plains with a desert cell at <see cref="desert"/>; section 1 is granite in
    /// Obsidian's previous format, by state id.
    /// </summary>
    private static NbtCompound VanillaChunk()
    {
        // 4 bits per block, 16 per long, from the lowest bits; index (y * 16 + z) * 16 + x.
        var blocks = new long[256];
        var stairsIndex = (stairs.Y * 16 + stairs.Z) * 16 + stairs.X;
        blocks[stairsIndex / 16] = 1L << (stairsIndex % 16 * 4);

        // 1 bit per biome cell, 64 per long; index (y * 4 + z) * 4 + x in 4-block cells.
        var desertIndex = ((desert.Y >> 2) * 4 + (desert.Z >> 2)) * 4 + (desert.X >> 2);

        var stairsProperties = new NbtCompound("Properties");
        foreach (var (name, value) in VanillaSaves.stairsProperties)
            stairsProperties.Add(new NbtTag<string>(name, value));

        return new NbtCompound
        {
            new NbtTag<int>("DataVersion", 4671),
            new NbtTag<int>("xPos", 0),
            new NbtTag<int>("zPos", 0),
            new NbtTag<int>("yPos", -4),
            new NbtTag<string>("Status", "minecraft:full"),
            // Data Obsidian doesn't model, which a save must keep: a scheduled repeater tick and a structure reference.
            new NbtList(NbtTagType.Compound, "block_ticks")
            {
                new NbtCompound
                {
                    new NbtTag<string>("i", "minecraft:repeater"),
                    new NbtTag<int>("x", 1),
                    new NbtTag<int>("y", 2),
                    new NbtTag<int>("z", 3),
                    new NbtTag<int>("t", 2),
                    new NbtTag<int>("p", 0)
                }
            },
            new NbtCompound("structures")
            {
                new NbtCompound("starts"),
                new NbtCompound("References") { new NbtArray<long>("minecraft:village_plains", [1L]) }
            },
            new NbtList(NbtTagType.Compound, "sections")
            {
                new NbtCompound
                {
                    new NbtTag<byte>("Y", 0),
                    new NbtCompound("block_states")
                    {
                        new NbtList(NbtTagType.Compound, "palette")
                        {
                            new NbtCompound { new NbtTag<string>("Name", "minecraft:stone") },
                            new NbtCompound { new NbtTag<string>("Name", "minecraft:oak_stairs"), stairsProperties }
                        },
                        new NbtArray<long>("data", blocks)
                    },
                    new NbtCompound("biomes")
                    {
                        new NbtList(NbtTagType.String, "palette")
                        {
                            new NbtTag<string>(string.Empty, "minecraft:plains"),
                            new NbtTag<string>(string.Empty, "minecraft:desert")
                        },
                        new NbtArray<long>("data", [1L << desertIndex])
                    }
                },
                new NbtCompound
                {
                    new NbtTag<byte>("Y", 1),
                    new NbtCompound("block_states")
                    {
                        new NbtList(NbtTagType.Compound, "palette")
                        {
                            new NbtCompound
                            {
                                new NbtTag<string>("Name", "minecraft:granite"),
                                new NbtTag<int>("Id", BlocksRegistry.Get(Material.Granite).GetHashCode())
                            }
                        }
                    }
                }
            }
        };
    }

    private static async Task WriteChunkAsync(string worldFolder, NbtCompound chunk)
    {
        var regionFolder = Path.Join(worldFolder, "region");
        Directory.CreateDirectory(regionFolder);

        await using var stream = new MemoryStream();
        await using (var writer = new NbtWriterStream(stream, NbtCompression.ZLib, ""))
        {
            foreach (var (_, tag) in chunk)
                writer.WriteTag(tag);

            writer.EndCompound();
            await writer.TryFinishAsync();
        }

        await using var file = new RegionFile(Path.Join(regionFolder, "r.0.0.mca"), NbtCompression.ZLib);
        await file.InitializeAsync();
        await file.SetChunkAsync(0, 0, stream.ToArray());
        file.Flush();
    }

    private static async Task<IChunk> LoadChunkAsync(string worldFolder)
    {
        await using var region = new Region(0, 0, worldFolder, "region");
        await region.InitAsync();

        return await region.GetChunkAsync(0, 0);
    }

    private static async Task<NbtCompound> ReadChunkAsync(string worldFolder)
    {
        await using var file = new RegionFile(Path.Join(worldFolder, "region", "r.0.0.mca"), NbtCompression.ZLib);
        await file.InitializeAsync();

        var bytes = (await file.GetChunkBytesAsync(0, 0))!.Value;
        return (NbtCompound)new NbtReader(new MemoryStream(bytes.ToArray())).ReadNextTag()!;
    }

    private sealed class FixedOptions(ServerConfiguration value) : IOptionsMonitor<ServerConfiguration>
    {
        public ServerConfiguration CurrentValue => value;

        public ServerConfiguration Get(string? name) => value;

        public IDisposable? OnChange(Action<ServerConfiguration, string?> listener) => null;
    }
}
