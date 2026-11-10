using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.BlockTicks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>frogspawn.json</c>: vanilla's frogspawn scheduling its hatch when placed and hatching (or not) on that
/// tick, from <c>Java/HatchingFixtures.java</c> (<c>CaptureFixtures=frogspawn</c>).
/// </summary>
public sealed class VanillaFrogspawn(ITestOutputHelper output)
{
    private static readonly JsonElement fixture = LoadFixture();

    /// <summary>
    /// Each case runs with its seed as the level's random: a placement through a block change with every update, and
    /// a tick through the level's scheduled-tick dispatch. The draws, blocks, scheduled ticks, sounds, level events
    /// and tadpoles must be vanilla's.
    /// </summary>
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ReplayFrogspawnReference()
    {
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        await using var probes = await BlockProbes.CreateAsync("frogspawn");

        // Each case moved along x from the last.
        var cases = fixture.GetProperty("cases").EnumerateArray()
            .Select((probe, index) => (Probe: probe, Offset: new Vector(index * BlockProbes.CaseSpacing, 0, 0)))
            .ToArray();
        FixtureCases.AssertAll(cases, item => Name(item.Probe), item => Replay(probes, item.Probe, item.Offset));

        output.WriteLine($"Replayed {cases.Length} frogspawn cases.");
    }

    /// <summary>
    /// Frogspawn a frog laid used to hatch on a countdown Obsidian saved as <c>ObsidianFrogspawnTicks</c>. A chunk saved
    /// with one comes back with it as the frogspawn's scheduled tick; saved again by this version and reloaded, the
    /// tick is still pending, and the old list isn't written back. In a level, the frogspawn then hatches once: its
    /// tadpoles appear, and further ticks neither hatch it again nor leave a hatch pending.
    /// </summary>
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task OlderSavesCountdownsHatchOnceAfterAReload()
    {
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-frogspawn-{Guid.NewGuid():N}");
        try
        {
            // An old save: frogspawn on water, its countdown due next tick in Obsidian's own list.
            var frogspawn = new Vector(2, 64, 3);
            var old = CompleteChunk(0, 0);
            old.SetBlock(frogspawn.X, frogspawn.Y - 1, frogspawn.Z, BlocksRegistry.Water);
            old.SetBlock(frogspawn.X, frogspawn.Y, frogspawn.Z, BlocksRegistry.Frogspawn);
            old.UnmodeledTags.Add(OldCountdowns(frogspawn, delay: 1));

            // Loaded, then saved by this version and loaded again.
            var migrated = await SaveAndReloadAsync(folder, old);
            var resaved = await SaveAndReloadAsync(folder, migrated);

            var pending = new SavedBlockTick(frogspawn, "minecraft:frogspawn", 1);
            Assert.Equal([pending], migrated.BlockTicks.Pack());
            Assert.Equal([pending], resaved.BlockTicks.Pack());
            Assert.DoesNotContain(resaved.UnmodeledTags, tag => tag.Name == "ObsidianFrogspawnTicks");

            // In a level with a player near, so it ticks.
            await using var context = TestServerFactory.CreateServer($"frogspawn-save-{Guid.NewGuid():N}");
            var world = context.World;
            world.LevelData.GameRules["spawn_mobs"] = "false";
            world.LevelData.GameRules["random_tick_speed"] = "0";
            LoadChunks(world, resaved);
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Keeper",
                (VectorD)frogspawn + new VectorD(0.5, 1, 0.5));
            Assert.True(world.TryAddPlayer(player));

            await world.DoWorldTickAsync();
            var tadpoles = Tadpoles(world, frogspawn);

            Assert.True(world.BlockUpdates.GetBlock(frogspawn).IsAir);
            Assert.InRange(tadpoles, 2, 5);

            for (var tick = 0; tick < 5; tick++)
                await world.DoWorldTickAsync();

            Assert.Equal(tadpoles, Tadpoles(world, frogspawn));
            Assert.DoesNotContain(resaved.BlockTicks.Pack(), tick => tick.Block == "minecraft:frogspawn");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    // Obsidian's frogspawn countdowns as versions before this one saved them.
    private static NbtList OldCountdowns(Vector position, int delay)
    {
        var countdown = new NbtCompound
        {
            new NbtTag<int>("x", position.X),
            new NbtTag<int>("y", position.Y),
            new NbtTag<int>("z", position.Z),
            new NbtTag<int>("delay", delay)
        };

        return new NbtList(NbtTagType.Compound, "ObsidianFrogspawnTicks") { countdown };
    }

    // Saves the chunk to region (0, 0) in the folder and loads it back.
    private static async Task<Chunk> SaveAndReloadAsync(string folder, Chunk chunk)
    {
        await using (var region = new Region(0, 0, folder))
        {
            await region.InitAsync();
            region.SetChunk(chunk);
            await region.FlushAsync();
        }

        await using var reopened = new Region(0, 0, folder);
        await reopened.InitAsync();

        return Assert.IsType<Chunk>(await reopened.GetChunkAsync(chunk.X, chunk.Z));
    }

    // The chunk and its eight neighbours, empty, complete and ticking in the world.
    private static void LoadChunks(WorldData.World world, Chunk center)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = chunkX == center.X && chunkZ == center.Z ? center : CompleteChunk(chunkX, chunkZ);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
                world.Fluids.Track(chunk);
            }
        }
    }

    private static Chunk CompleteChunk(int x, int z)
    {
        var chunk = new Chunk(x, z);
        chunk.SetChunkStatus(ChunkGenStage.full);

        return chunk;
    }

    private static int Tadpoles(WorldData.World world, Vector position) =>
        world.GetNonPlayerEntitiesInRange((VectorD)position, 8).OfType<Tadpole>().Count();

    private static string Name(JsonElement probe) => probe.GetProperty("name").GetString()!;

    private static void Replay(BlockProbes probes, JsonElement probe, Vector offset)
    {
        var level = probes.Level(probe);
        BlockProbes.Setup(level, probe, offset);

        var origin = BlockProbes.Position(probe.GetProperty("origin")) + offset;
        var random = new RecordingRandom(probe.GetProperty("seed").GetInt64());
        level.GrowthRandom = random;
        probes.Listen(level, origin);

        if (probe.TryGetProperty("place", out var place))
            level.BlockUpdates.SetBlock(origin, BlockProbes.ParseState(place.GetString()!));
        else
            level.BlockUpdates.RunScheduledTick(origin, level.BlockUpdates.GetBlock(origin).UnlocalizedName);

        var differences = new List<string>();
        probes.Compare(differences, probe, level, offset, random);
        CompareTadpoles(differences, probe.GetProperty("entities"), level, origin, offset);

        if (differences.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", differences));
    }

    private static void CompareTadpoles(
        List<string> differences,
        JsonElement expected,
        AbstractLevel level,
        Vector origin,
        Vector offset)
    {
        var vanilla = expected.EnumerateArray()
            .Select(tadpole => Tadpole(
                BlockProbes.Point(tadpole.GetProperty("pos")) + (VectorD)offset,
                tadpole.GetProperty("yaw").GetSingle(),
                tadpole.GetProperty("persistent").GetBoolean()))
            .Order()
            .ToList();

        var obsidian = level.GetNonPlayerEntitiesInRange((VectorD)origin, 2)
            .OfType<Tadpole>()
            .Select(tadpole => Tadpole(tadpole.Position, tadpole.Yaw, tadpole.PersistenceRequired))
            .Order()
            .ToList();

        if (!vanilla.SequenceEqual(obsidian))
        {
            differences.Add(
                $"tadpoles: vanilla [{string.Join(", ", vanilla)}], Obsidian [{string.Join(", ", obsidian)}]");
        }
    }

    // Yaws as entities hold them, in 256ths of a turn.
    private static string Tadpole(VectorD position, Angle yaw, bool persistent) =>
        $"({position.X:0.######}, {position.Y:0.######}, {position.Z:0.######}) yaw {yaw.Value} persistent {persistent}";

    private static JsonElement LoadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Tests.Assets.frogspawn.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
