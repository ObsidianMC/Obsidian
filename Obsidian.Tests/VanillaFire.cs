using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>fire_ticks.json</c>, vanilla's fire tick and placement captured with a seeded level random (see
/// <c>docs/scenario-captures.md</c>): the draws in order, the blocks touched, the ticks scheduled and the entities
/// added, for every seed of every case.
/// </summary>
public sealed class VanillaFire(ITestOutputHelper output)
{
    // Each run gets its own spot in one shared world, this far from the next, so that neither fire's reach nor a
    // case's blocks touch another run's.
    private const int Spacing = 8;
    private const int SpotsPerRow = 15;
    private const int FirstSpot = -56;
    private const int SpotY = 100;

    // How far a run's blocks, ticks and entities reach from its origin.
    private const int Reach = 3;

    private sealed record Run(JsonElement Case, JsonElement Captured, Vector Origin)
    {
        public string Name => $"{this.Case.GetProperty("name").GetString()}#{this.Captured.GetProperty("seed")}";
    }

    // What Obsidian did in a run, read right after it. Blocks and dropped items are read once the level has ticked:
    // Obsidian removes a block that a shape update destroys, and drops a removed block entity's items, later in the
    // level's tick, where vanilla does both at once.
    private sealed record Observed(Run Run, string[] Draws, int NextRandom, string[] Ticks, string[] Tnt);

    [Fact]
    public async Task ReplayFireReference()
    {
        using var stream = typeof(VanillaFire).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.fire_ticks.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var runs = Runs(root).ToArray();
        await using var context = TestServerFactory.CreateServer($"fire-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.GameRules["spawn_mobs"] = "false";
        world.LevelData.GameRules["random_tick_speed"] = "0";
        // The probe's fire spreads with no player around.
        world.LevelData.GameRules["fire_spread_radius_around_player"] = "-1";
        await AddChunksAsync(world);

        var observed = new List<Observed>();
        foreach (var run in runs)
            observed.Add(await ReplayAsync(world, run));

        // The destruction and drops the runs queued.
        await world.DoWorldTickAsync();

        FixtureCases.AssertAll(observed, seen => seen.Run.Name, seen => AssertMatches(world, seen));
        output.WriteLine($"Replayed {runs.Length} fire runs.");
    }

    private static IEnumerable<Run> Runs(JsonElement root)
    {
        var index = 0;
        foreach (var fireCase in root.GetProperty("cases").EnumerateArray())
        {
            foreach (var captured in fireCase.GetProperty("runs").EnumerateArray())
            {
                var x = FirstSpot + index % SpotsPerRow * Spacing;
                var z = FirstSpot + index / SpotsPerRow * Spacing;
                index++;
                yield return new Run(fireCase, captured, new Vector(x, SpotY, z));
            }
        }
    }

    private static async Task<Observed> ReplayAsync(WorldData.World world, Run run)
    {
        var fireCase = run.Case;
        var origin = run.Origin;
        var setup = fireCase.GetProperty("setup").EnumerateArray()
            .Select(block => (At: Offset(block.GetProperty("at")), State: Parse(block.GetProperty("state"))))
            .ToArray();

        // The case's blocks, without updates, then the environment the probe gave vanilla.
        foreach (var (at, state) in setup)
        {
            world.BlockUpdates.SetBlock(origin + at, state, BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
            if (world.BlockUpdates.GetBlockEntity(origin + at) is LecternBlockEntity lectern
                && state.GetProperty("has_book") == "true")
            {
                lectern.Lectern.SetItem(LecternContainer.BookSlot,
                    new ItemStack(ItemsRegistry.Get("minecraft:writable_book"), 1));
            }
        }

        PrepareEnvironment(world, run, setup);

        // The run, with a level random that records its draws.
        var random = new RecordingRandom(run.Captured.GetProperty("seed").GetInt64());
        world.GrowthRandom = random;
        if (fireCase.GetProperty("kind").GetString() == "tick")
        {
            world.BlockUpdates.RunScheduledTick(origin, world.BlockUpdates.GetBlock(origin).UnlocalizedName);
        }
        else
        {
            // The path explosions, fireballs and end crystals set their fire through.
            await world.SetBlockAsync(origin, Parse(fireCase.GetProperty("place")), BlockUpdateFlags.All);
        }

        var draws = random.Draws.ToArray();
        var nextRandom = random.Inner.NextInt();

        return new Observed(run, draws, nextRandom, await TicksAsync(world, origin), Tnt(world, origin));
    }

    // Rain, difficulty, the dimension's infiniburn blocks and the biome; rain reaches the blocks with nothing that
    // blocks motion above them among the case's, as in the probe.
    private static void PrepareEnvironment(WorldData.World world, Run run, (Vector At, IBlock State)[] setup)
    {
        var fireCase = run.Case;
        world.LevelData.Raining = fireCase.TryGetProperty("raining", out var raining) && raining.GetBoolean();
        var difficulty = StringOr(fireCase, "difficulty", "normal");
        world.LevelData.Difficulty = Enum.Parse<Difficulty>(difficulty, ignoreCase: true);

        var dimension = StringOr(fireCase, "dimension", "minecraft:overworld");
        Assert.True(CodecRegistry.TryGetDimension(dimension, out var dimensionCodec));
        world.DimensionType.Element.Infiniburn = dimensionCodec!.Element.Infiniburn;

        var biomeName = StringOr(fireCase, "biome", "minecraft:plains");
        Assert.True(CodecRegistry.TryGetBiome(biomeName, out var biome));

        for (var dx = -Reach; dx <= Reach; dx++)
        {
            for (var dz = -Reach; dz <= Reach; dz++)
            {
                for (var dy = -Reach; dy <= Reach + 3; dy++)
                {
                    var at = new Vector(dx, dy, dz);
                    var position = run.Origin + at;
                    var chunk = world.GetLoadedChunk(position.X >> 4, position.Z >> 4)!;
                    chunk.SetBiome(position.X, position.Y, position.Z, biome!);

                    var covered = setup.Any(block => block.At.X == dx && block.At.Z == dz && block.At.Y > dy
                        && BlockCollisionShapes.Get(block.State).Count > 0);
                    chunk.SetLightLevel(position.X, position.Y, position.Z, LightType.Sky, covered ? 0 : 15);
                }
            }
        }
    }

    // The block ticks scheduled within the run's reach, relative to its origin, sorted.
    private static async Task<string[]> TicksAsync(WorldData.World world, Vector origin)
    {
        var ticks = new List<string>();
        var chunks = new HashSet<(int, int)>();
        for (var dx = -Reach; dx <= Reach; dx += Reach)
        {
            for (var dz = -Reach; dz <= Reach; dz += Reach)
                chunks.Add(((origin.X + dx) >> 4, (origin.Z + dz) >> 4));
        }

        foreach (var (chunkX, chunkZ) in chunks)
        {
            var chunk = Assert.IsType<Chunk>(await world.GetChunkAsync(chunkX, chunkZ));
            foreach (var tick in chunk.BlockTicks.Pack())
            {
                var at = tick.Position - origin;
                if (Math.Abs(at.X) <= Reach && Math.Abs(at.Z) <= Reach && Math.Abs(at.Y) <= Reach + 3)
                    ticks.Add(Tick(at, tick.Block, tick.Delay));
            }
        }

        ticks.Sort(StringComparer.Ordinal);
        return [.. ticks];
    }

    private static string[] Tnt(WorldData.World world, Vector origin) =>
        [.. world.GetEntitiesInRange((VectorD)origin, Reach).OfType<PrimedTnt>()
            .Select(tnt => Location(tnt.Position - (VectorD)origin))
            .Order(StringComparer.Ordinal)];

    private static void AssertMatches(WorldData.World world, Observed seen)
    {
        var captured = seen.Run.Captured;
        var draws = captured.GetProperty("draws").EnumerateArray().Select(draw => draw.GetString()!).ToArray();
        Assert.Equal(draws, seen.Draws);
        Assert.Equal(captured.GetProperty("nextRandom").GetInt32(), seen.NextRandom);

        foreach (var block in captured.GetProperty("after").EnumerateArray())
        {
            var at = Offset(block.GetProperty("at"));
            var expected = Parse(block.GetProperty("state"));
            var actual = world.BlockUpdates.GetBlock(seen.Run.Origin + at);
            var message = $"{at}: expected {block.GetProperty("state")}, got {Format(actual)}";
            Assert.True(expected.IsSameState(actual), message);
        }

        var ticks = captured.GetProperty("ticks").EnumerateArray()
            .Select(tick => Tick(Offset(tick.GetProperty("at")), tick.GetProperty("block").GetString()!,
                tick.GetProperty("delay").GetInt32()))
            .Order(StringComparer.Ordinal);
        Assert.Equal(ticks, seen.Ticks);

        var entities = captured.GetProperty("entities").EnumerateArray().ToArray();
        var tnt = entities.Where(entity => entity.GetProperty("type").GetString() == "minecraft:tnt")
            .Select(entity => Location(Point(entity.GetProperty("pos"))))
            .Order(StringComparer.Ordinal);
        Assert.Equal(tnt, seen.Tnt);

        // Obsidian drops a removed block entity's items on the next tick, where vanilla's lectern pops its book at
        // once, so only the items themselves are compared.
        var items = entities.Where(entity => entity.GetProperty("type").GetString() == "minecraft:item")
            .Select(entity => $"{entity.GetProperty("item").GetString()} x{entity.GetProperty("count").GetInt32()}")
            .Order(StringComparer.Ordinal);
        var dropped = world.GetEntitiesInRange((VectorD)seen.Run.Origin, Reach + 1).OfType<ItemEntity>()
            .Select(item => $"{item.Item.Holder.UnlocalizedName} x{item.Item.Count}")
            .Order(StringComparer.Ordinal);
        Assert.Equal(items, dropped);
    }

    private static string Tick(Vector at, string block, int delay) => $"{at} {block} in {delay}";

    private static string Location(VectorD at) => $"({at.X:0.###}, {at.Y:0.###}, {at.Z:0.###})";

    private static IBlock Parse(JsonElement state) => VanillaSulfurBlocks.ParseState(state.GetString()!);

    private static string Format(IBlock state) =>
        state.UnlocalizedName + "["
        + string.Join(',', state.GetProperties().Select(pair => $"{pair.Key}={pair.Value}")) + "]";

    private static string StringOr(JsonElement fireCase, string key, string fallback) =>
        fireCase.TryGetProperty(key, out var value) ? value.GetString()! : fallback;

    private static Vector Offset(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());

    private static VectorD Point(JsonElement values) =>
        new(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble());

    // Empty, complete chunks around the runs' spots, with a ring around them so that every spot's chunk ticks.
    private static async Task AddChunksAsync(WorldData.World world)
    {
        for (var chunkX = -5; chunkX <= 5; chunkX++)
        {
            for (var chunkZ = -5; chunkZ <= 5; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ);
                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
            }
        }

        for (var chunkX = -5; chunkX <= 5; chunkX++)
        {
            for (var chunkZ = -5; chunkZ <= 5; chunkZ++)
                await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
        }
    }

    /// <summary>A legacy random that records each draw, named as the Java probe names them.</summary>
    private sealed class RecordingRandom(long seed) : IRandomSource
    {
        public LegacyRandomSource Inner { get; } = new(seed);

        public List<string> Draws { get; } = [];

        public IRandomSource Fork() => this.Record("fork", this.Inner.Fork());

        public IPositionalRandomFactory ForkPositional() => this.Record("forkPositional", this.Inner.ForkPositional());

        public void SetSeed(long seed) => this.Inner.SetSeed(seed);

        public int NextInt()
        {
            var value = this.Inner.NextInt();
            this.Draws.Add($"int={value}");
            return value;
        }

        public int NextInt(int bound)
        {
            var value = this.Inner.NextInt(bound);
            this.Draws.Add($"int({bound})={value}");
            return value;
        }

        public int NextInt(int origin, int bound) =>
            this.Record($"int({origin}, {bound})", this.Inner.NextInt(origin, bound));

        public int NextIntBetweenInclusive(int min, int max) =>
            this.Record($"intBetween({min}, {max})", this.Inner.NextIntBetweenInclusive(min, max));

        public long NextLong() => this.Record("long", this.Inner.NextLong());

        public bool NextBoolean() => this.Record("boolean", this.Inner.NextBoolean());

        public float NextFloat() => this.Record("float", this.Inner.NextFloat());

        public double NextDouble() => this.Record("double", this.Inner.NextDouble());

        public double NextGaussian() => this.Record("gaussian", this.Inner.NextGaussian());

        public double Triangle(double mode, double deviation) =>
            this.Record("triangle", this.Inner.Triangle(mode, deviation));

        public float Triangle(float mode, float deviation) =>
            this.Record("triangle", this.Inner.Triangle(mode, deviation));

        public void ConsumeCount(int count)
        {
            this.Draws.Add($"consume({count})");
            this.Inner.ConsumeCount(count);
        }

        private T Record<T>(string draw, T value)
        {
            this.Draws.Add(draw);
            return value;
        }
    }
}
