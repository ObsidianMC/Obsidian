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
    /// with those countdowns comes back with each as the frogspawn's scheduled tick, so it still hatches.
    /// </summary>
    [Fact]
    public async Task OlderSavesCountdownsBecomeHatchTicks()
    {
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-frogspawn-{Guid.NewGuid():N}");
        try
        {
            // Chunk (35, 40) lies in region (1, 1), so the stored coordinates are absolute.
            var position = new Vector(35 * 16 + 2, 64, 40 * 16 + 3);
            var chunk = new Chunk(35, 40);
            chunk.SetChunkStatus(ChunkGenStage.full);
            chunk.UnmodeledTags.Add(new NbtList(NbtTagType.Compound, "ObsidianFrogspawnTicks")
            {
                new NbtCompound
                {
                    new NbtTag<int>("x", position.X),
                    new NbtTag<int>("y", position.Y),
                    new NbtTag<int>("z", position.Z),
                    new NbtTag<int>("delay", 1234)
                }
            });

            await using (var region = new Region(1, 1, folder))
            {
                await region.InitAsync();
                region.SetChunk(chunk);
                await region.FlushAsync();
            }

            await using var reopened = new Region(1, 1, folder);
            await reopened.InitAsync();
            var localX = 35 - (1 << Region.CubicRegionSizeShift);
            var localZ = 40 - (1 << Region.CubicRegionSizeShift);
            var loaded = Assert.IsType<Chunk>(await reopened.GetChunkAsync(localX, localZ));

            var tick = Assert.Single(loaded.BlockTicks.Pack());
            Assert.Equal(new SavedBlockTick(position, "minecraft:frogspawn", 1234), tick);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

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
