using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Chunks in the shapes vanilla 1.21.11 saves them, built here from those shapes (not captured saves).
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

    // Where the synthetic chunk has its stairs and desert, in section 0 of chunk (0, 0).
    private static readonly Vector stairs = new(3, 5, 7);
    private static readonly Vector desert = new(4, 0, 8);

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-vanilla-saves-{Guid.NewGuid():N}");

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

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
    }

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
}
