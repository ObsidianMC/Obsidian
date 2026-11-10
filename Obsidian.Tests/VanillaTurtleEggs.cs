using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>turtle_eggs.json</c>: vanilla's turtle eggs on random ticks and on being placed, and their hatch
/// chance over the day, from <c>Java/HatchingFixtures.java</c> (<c>CaptureFixtures=turtle_eggs</c>).
/// </summary>
public sealed class VanillaTurtleEggs(ITestOutputHelper output)
{
    private static readonly JsonElement fixture = LoadFixture();

    /// <summary>
    /// Each case runs where it says, at its clock time and with its seed: a random tick goes through Obsidian's
    /// dispatch (whether the state ticks, then the owner the generator binds it to) and a placement through a block
    /// change with every update. The draws, blocks, scheduled ticks, sounds, level events and hatched turtles must be
    /// vanilla's, and so must the hatch chance at each sampled time.
    /// </summary>
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ReplayTurtleEggReference()
    {
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        await using var probes = await BlockProbes.CreateAsync("turtle-eggs", "minecraft:the_nether",
            "minecraft:the_end");

        // The cases, each moved along x from the last, then the chance samples, all checked before failing once.
        var cases = fixture.GetProperty("cases").EnumerateArray()
            .Select((probe, index) => new Check(
                probe.GetProperty("name").GetString()!,
                () => Replay(probes, probe, new Vector(index * BlockProbes.CaseSpacing, 0, 0))))
            .ToArray();
        var chances = fixture.GetProperty("chances").EnumerateArray()
            .Select(sample => new Check(Describe(sample), () => CheckChance(probes, sample)))
            .ToArray();

        FixtureCases.AssertAll(cases.Concat(chances), check => check.Name, check => check.Run());

        output.WriteLine($"Replayed {cases.Length} turtle egg cases and {chances.Length} hatch chances.");
    }

    private sealed record Check(string Name, Action Run);

    private static string Describe(JsonElement sample) =>
        $"chance in {sample.GetProperty("dimension").GetString()} at {sample.GetProperty("time").GetInt64()}";

    private static void Replay(BlockProbes probes, JsonElement probe, Vector offset)
    {
        var level = probes.Level(probe);
        SetTime(probes.World, probe.TryGetProperty("time", out var time) ? time.GetInt64() : 0);

        BlockProbes.Setup(level, probe, offset);

        var origin = BlockProbes.Position(probe.GetProperty("origin")) + offset;
        var random = new RecordingRandom(probe.GetProperty("seed").GetInt64());
        level.GrowthRandom = random;
        probes.Listen(level, origin);

        var differences = new List<string>();
        if (probe.TryGetProperty("place", out var place))
            level.BlockUpdates.SetBlock(origin, BlockProbes.ParseState(place.GetString()!));
        else
            RandomTick(differences, probe, level, origin, random);

        probes.Compare(differences, probe, level, offset, random);
        CompareTurtles(differences, probe.GetProperty("entities"), level, origin, offset);

        if (differences.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", differences));
    }

    // AbstractLevel.TickChunkBlocks: only a randomly ticking state ticks, through its generated owner.
    private static void RandomTick(
        List<string> differences,
        JsonElement probe,
        AbstractLevel level,
        Vector origin,
        RecordingRandom random)
    {
        var block = level.BlockUpdates.GetBlock(origin);
        var ticks = BlockPhysics.IsRandomlyTicking(block.GetHashCode());
        BlockProbes.Expect(differences, "ticks", probe.GetProperty("ticks").GetBoolean(), ticks);
        if (!ticks)
            return;

        var behavior = Growth.VanillaRandomTick(block);
        if (behavior is null)
            differences.Add($"{block.UnlocalizedName} has no random tick owner");
        else
            behavior.RandomTick(level, origin, block, random);
    }

    private static void CompareTurtles(
        List<string> differences,
        JsonElement expected,
        AbstractLevel level,
        Vector origin,
        Vector offset)
    {
        var vanilla = expected.EnumerateArray()
            .Select(turtle => Turtle(
                BlockProbes.Point(turtle.GetProperty("pos")) + (VectorD)offset,
                turtle.GetProperty("yaw").GetSingle(),
                turtle.GetProperty("age").GetInt32(),
                BlockProbes.Position(turtle.GetProperty("home")) + offset))
            .ToList();

        var obsidian = level.GetNonPlayerEntitiesInRange((VectorD)origin, 2)
            .OfType<Turtle>()
            .OrderBy(turtle => turtle.Position.X)
            .Select(turtle => Turtle(turtle.Position, turtle.Yaw, turtle.Age, turtle.Home))
            .ToList();

        if (!vanilla.SequenceEqual(obsidian))
            differences.Add($"turtles: vanilla [{string.Join(", ", vanilla)}], Obsidian [{string.Join(", ", obsidian)}]");
    }

    // Yaws as entities hold them, in 256ths of a turn.
    private static string Turtle(VectorD position, Angle yaw, int age, Vector home) =>
        $"({position.X:0.###}, {position.Y:0.###}, {position.Z:0.###}) yaw {yaw.Value} age {age} home {home}";

    private static void CheckChance(BlockProbes probes, JsonElement sample)
    {
        var dimension = sample.GetProperty("dimension").GetString()!;
        var level = probes.Level(dimension);
        SetTime(probes.World, sample.GetProperty("time").GetInt64());

        var chance = level.TurtleEggHatchChance(new Vector(0, 64, 0));

        Assert.Equal(sample.GetProperty("chance").GetSingle(), chance);
    }

    // The overworld's clock, which the day timeline follows in every level.
    private static void SetTime(WorldData.World world, long time) =>
        world.LevelData.WorldClocks.States[WorldClocks.Overworld].TotalTicks = time;

    private static JsonElement LoadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Tests.Assets.turtle_eggs.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
