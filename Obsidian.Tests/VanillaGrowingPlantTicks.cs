using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>growing_plant_ticks.json</c>: vanilla's <c>randomTick</c> of growing plants, from
/// <c>Java/GrowingPlantTickFixtures.java</c> (<c>CaptureFixtures=growing_plant_ticks</c>).
/// </summary>
public sealed class VanillaGrowingPlantTicks(ITestOutputHelper output)
{
    private static readonly JsonElement fixture = LoadFixture();

    /// <summary>
    /// Each case's state goes through Obsidian's random-tick dispatch (whether the state ticks, then the owner the
    /// generator binds it to) in a fresh world holding only the case's blocks, with the case's seed (the level's random
    /// too) and light, and then the case's actions. The draws, the ordered writes with their flags, the final blocks
    /// and the scheduled ticks with their delays must be vanilla's, and nothing else around may change.
    /// </summary>
    [Fact]
    public async Task ReplayGrowingPlantTickReference()
    {
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        var cases = fixture.GetProperty("cases").EnumerateArray().ToArray();
        await FixtureCases.AssertAllAsync(cases, Name, ReplayAsync);

        output.WriteLine($"Replayed {cases.Length} growing plant cases.");
    }

    private static string Name(JsonElement probe) => probe.GetProperty("name").GetString()!;

    private static async Task ReplayAsync(JsonElement probe)
    {
        await using var context = TestServerFactory.CreateServer($"growing-plants-{Guid.NewGuid():N}");
        var world = context.World;
        await SetUpAsync(world, probe);

        var before = Snapshot(world, probe);
        var random = new RecordingRandom(probe.GetProperty("seed").GetInt64());
        world.GrowthRandom = random;
        var writes = new List<Write>();
        world.Fluids.BlockStored = (position, block, flags) =>
            writes.Add(new Write(position, block, flags));
        var differences = new List<string>();

        if (probe.TryGetProperty("origin", out var origin))
            Tick(differences, world, Position(origin), probe.GetProperty("ticks").GetBoolean(), random);

        // Then what the player or the world does next, as Level.setBlock or removeBlock would.
        if (probe.TryGetProperty("actions", out var actions))
        {
            foreach (var action in actions.EnumerateArray())
            {
                var position = Position(action.GetProperty("pos"));
                if (action.TryGetProperty("state", out var state))
                    world.BlockUpdates.SetBlock(position, VanillaSulfurBlocks.ParseState(state.GetString()!));
                else
                    world.BlockUpdates.RemoveBlock(position);
            }
        }

        world.Fluids.BlockStored = null;

        CompareDraws(differences, probe.GetProperty("draws"), random.Draws);
        CompareWrites(differences, probe.GetProperty("writes"), writes);
        CompareBlocks(differences, probe.GetProperty("after"), world, before);
        CompareTicks(differences, probe.GetProperty("scheduled"), world);

        if (differences.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", differences));
    }

    // The case's rules and blocks, written without updates as vanilla's block view holds them, and its light at the
    // origin, the only light read. The light comes after the blocks, since writing them relights.
    private static async Task SetUpAsync(WorldData.World world, JsonElement probe)
    {
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";
        if (probe.TryGetProperty("spreadVines", out var spreadVines))
            world.LevelData.GameRules["spread_vines"] = spreadVines.GetBoolean() ? "true" : "false";

        await AddEmptyChunksAsync(world);

        foreach (var placement in probe.GetProperty("setup").EnumerateArray())
        {
            var state = VanillaSulfurBlocks.ParseState(placement.GetProperty("state").GetString()!);
            world.BlockUpdates.SetBlock(Position(placement.GetProperty("pos")), state,
                BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        }

        if (!probe.TryGetProperty("origin", out var given))
            return;

        var origin = Position(given);
        var light = probe.TryGetProperty("light", out var level) ? level.GetInt32() : 15;
        var chunk = world.GetLoadedChunk(origin.X >> 4, origin.Z >> 4)!;
        chunk.SetLightLevel(origin.X, origin.Y, origin.Z, LightType.Sky, light);
        chunk.SetLightLevel(origin.X, origin.Y, origin.Z, LightType.Block, 0);
    }

    // AbstractLevel.TickChunkBlocks: only a randomly ticking state ticks, through its generated owner.
    private static void Tick(List<string> differences, WorldData.World world, Vector origin, bool vanillaTicks,
        IRandomSource random)
    {
        var block = world.BlockUpdates.GetBlock(origin);
        var ticks = BlockPhysics.IsRandomlyTicking(block.GetHashCode());
        Expect(differences, "ticks", vanillaTicks, ticks);
        if (!ticks)
            return;

        if (Growth.VanillaRandomTick(block) is IRandomTickBehavior behavior)
            behavior.RandomTick(world, origin, block, random);
        else
            differences.Add($"{block.UnlocalizedName} has no random tick owner");
    }

    private static void CompareDraws(List<string> differences, JsonElement expected, List<Draw> actual)
    {
        var draws = expected.EnumerateArray().Select(Draw.Parse).ToList();
        if (!draws.SequenceEqual(actual))
            differences.Add($"draws: vanilla [{string.Join(", ", draws)}], Obsidian [{string.Join(", ", actual)}]");
    }

    // Every block the tick and the actions stored, in order, with the flags it was stored with.
    private static void CompareWrites(List<string> differences, JsonElement expected, List<Write> actual)
    {
        var vanilla = expected.EnumerateArray()
            .Select(write => new Write(
                Position(write.GetProperty("pos")),
                VanillaSulfurBlocks.ParseState(write.GetProperty("state").GetString()!),
                (BlockUpdateFlags)write.GetProperty("flags").GetInt32()))
            .ToList();

        var same = vanilla.Count == actual.Count
            && vanilla.Zip(actual).All(pair => pair.First.Matches(pair.Second));
        if (!same)
            differences.Add($"writes: vanilla [{string.Join(", ", vanilla)}], Obsidian [{string.Join(", ", actual)}]");
    }

    // The final state of every position vanilla set up or wrote, and no change anywhere else around them.
    private static void CompareBlocks(List<string> differences, JsonElement expected, WorldData.World world,
        Dictionary<Vector, IBlock> before)
    {
        var expectedPositions = new HashSet<Vector>();
        foreach (var placement in expected.EnumerateArray())
        {
            var position = Position(placement.GetProperty("pos"));
            var state = placement.GetProperty("state").GetString()!;
            var actual = world.BlockUpdates.GetBlock(position);
            expectedPositions.Add(position);
            if (!VanillaSulfurBlocks.ParseState(state).IsSameState(actual))
                differences.Add($"{position}: vanilla {state}, Obsidian {actual}");
        }

        foreach (var (position, old) in before)
        {
            var now = world.BlockUpdates.GetBlock(position);
            if (!expectedPositions.Contains(position) && !now.IsSameState(old))
                differences.Add($"{position}: unchanged in vanilla, Obsidian {old} -> {now}");
        }
    }

    // The blocks around the case: its chunk, from two below its lowest block to two above its highest.
    private static Dictionary<Vector, IBlock> Snapshot(WorldData.World world, JsonElement probe)
    {
        var heights = probe.GetProperty("setup").EnumerateArray()
            .Select(placement => placement.GetProperty("pos")[1].GetInt32())
            .ToList();
        var bottom = Math.Max(world.MinY, heights.Min() - 2);
        var top = Math.Min(world.MinY + world.Height - 1, heights.Max() + 2);

        var blocks = new Dictionary<Vector, IBlock>();
        for (var y = bottom; y <= top; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var position = new Vector(x, y, z);
                    blocks[position] = world.BlockUpdates.GetBlock(position);
                }
            }
        }

        return blocks;
    }

    // Every block and fluid tick pending in the case's chunk, with its delay, against those vanilla scheduled. Vanilla
    // records each scheduleTick call; the level keeps one tick per position and type.
    private static void CompareTicks(List<string> differences, JsonElement expected, WorldData.World world)
    {
        var vanilla = expected.EnumerateArray()
            .Select(tick => TickKey(Position(tick.GetProperty("pos")), tick.GetProperty("type").GetString()!,
                tick.GetProperty("delay").GetInt32()))
            .Distinct()
            .Order()
            .ToList();

        var chunk = (Chunk)world.GetLoadedChunk(0, 0)!;
        var blockTicks = chunk.BlockTicks.Pack().Select(tick => TickKey(tick.Position, tick.Block, tick.Delay));
        var fluidTicks = chunk.FluidTicks.Pack()
            .Select(tick => TickKey(tick.Position, FluidName(tick.Type), tick.Delay));
        var obsidian = blockTicks.Concat(fluidTicks).Order().ToList();

        if (!vanilla.SequenceEqual(obsidian))
            differences.Add($"ticks: vanilla [{string.Join(", ", vanilla)}], Obsidian [{string.Join(", ", obsidian)}]");
    }

    private static string TickKey(Vector position, string type, int delay) => $"{position} {type} +{delay}";

    private static string FluidName(FluidKind fluid) => fluid switch
    {
        FluidKind.Water => "minecraft:water",
        FluidKind.FlowingWater => "minecraft:flowing_water",
        FluidKind.Lava => "minecraft:lava",
        FluidKind.FlowingLava => "minecraft:flowing_lava",
        _ => fluid.ToString()
    };

    private static void Expect<T>(List<string> differences, string what, T vanilla, T obsidian)
    {
        if (!EqualityComparer<T>.Default.Equals(vanilla, obsidian))
            differences.Add($"{what}: vanilla {vanilla}, Obsidian {obsidian}");
    }

    // The nine chunks around the origin's, empty and complete, so writes and reads reach them.
    private static async Task AddEmptyChunksAsync(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ);
                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
            }
        }

        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
        }
    }

    private static Vector Position(JsonElement array) =>
        new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    private static JsonElement LoadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Tests.Assets.growing_plant_ticks.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// A block stored at a position, with every update flag it was stored with (Obsidian's flags share vanilla's bits).
    /// </summary>
    private sealed record Write(Vector Position, IBlock State, BlockUpdateFlags Flags)
    {
        public bool Matches(Write other) =>
            this.Position == other.Position && this.State.IsSameState(other.State) && this.Flags == other.Flags;

        public override string ToString() => $"{this.Position} {this.State} {(int)this.Flags}";
    }

    /// <summary>
    /// A draw as the probe records it: the call, its bound for <c>nextInt(bound)</c>, and its result.
    /// </summary>
    private sealed record Draw(string Call, int? Bound, string Value)
    {
        public static Draw Parse(JsonElement draw)
        {
            var call = draw.GetProperty("call").GetString()!;
            int? bound = draw.TryGetProperty("bound", out var given) ? given.GetInt32() : null;
            var value = draw.GetProperty("value");
            var text = call switch
            {
                "nextBoolean" => value.GetBoolean().ToString(),
                "nextFloat" => value.GetSingle().ToString("R", CultureInfo.InvariantCulture),
                "nextDouble" => value.GetDouble().ToString("R", CultureInfo.InvariantCulture),
                _ => value.GetInt64().ToString(CultureInfo.InvariantCulture)
            };

            return new Draw(call, bound, text);
        }

        public override string ToString() => Bound is int bound ? $"{Call}({bound})={Value}" : $"{Call}={Value}";
    }

    /// <summary>A legacy random that records each draw the way the probe does.</summary>
    private sealed class RecordingRandom(long seed) : IRandomSource
    {
        private readonly LegacyRandomSource source = new(seed);

        public List<Draw> Draws { get; } = [];

        public IRandomSource Fork() => throw new NotSupportedException();

        public IPositionalRandomFactory ForkPositional() => throw new NotSupportedException();

        public void SetSeed(long seed) => throw new NotSupportedException();

        public int NextInt() => this.Record("nextInt", null, this.source.NextInt());

        public int NextInt(int bound) => this.Record("nextInt", bound, this.source.NextInt(bound));

        public int NextInt(int origin, int bound) => origin + this.NextInt(bound - origin);

        public int NextIntBetweenInclusive(int min, int max) => this.NextInt(max - min + 1) + min;

        public long NextLong()
        {
            var value = this.source.NextLong();
            this.Draws.Add(new Draw("nextLong", null, value.ToString(CultureInfo.InvariantCulture)));
            return value;
        }

        public bool NextBoolean()
        {
            var value = this.source.NextBoolean();
            this.Draws.Add(new Draw("nextBoolean", null, value.ToString()));
            return value;
        }

        public float NextFloat()
        {
            var value = this.source.NextFloat();
            this.Draws.Add(new Draw("nextFloat", null, value.ToString("R", CultureInfo.InvariantCulture)));
            return value;
        }

        public double NextDouble()
        {
            var value = this.source.NextDouble();
            this.Draws.Add(new Draw("nextDouble", null, value.ToString("R", CultureInfo.InvariantCulture)));
            return value;
        }

        public double NextGaussian() => throw new NotSupportedException();

        public double Triangle(double mode, double deviation) => throw new NotSupportedException();

        public float Triangle(float mode, float deviation) => throw new NotSupportedException();

        public void ConsumeCount(int count) => throw new NotSupportedException();

        private int Record(string call, int? bound, int value)
        {
            this.Draws.Add(new Draw(call, bound, value.ToString(CultureInfo.InvariantCulture)));
            return value;
        }
    }
}
