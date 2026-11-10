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
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>ticking_blocks.json</c>: vanilla's random ticks, scheduled ticks, block changes and landings of pointed
/// dripstone, cauldrons, budding amethyst and the smaller ticking blocks, from <c>Java/TickingBlockFixtures.java</c>
/// (<c>CaptureFixtures=ticking_blocks</c>).
/// </summary>
public sealed class VanillaTickingBlocks(ITestOutputHelper output)
{
    // Vanilla's LevelEvent.PARTICLES_DESTROY_BLOCK, which the probe records as a destroyed block instead.
    private const int DestroyEvent = 2001;

    private static readonly JsonElement fixture = LoadFixture();

    /// <summary>
    /// Each case runs through Obsidian's dispatch (the owner the generator binds the block's class to, or a live
    /// block change) in a fresh world holding only the case's blocks, with the case's seed as the level's random. The
    /// draws, the blocks, the scheduled ticks, the level events clients get, the falling blocks and the blocks
    /// destroyed must be vanilla's.
    /// </summary>
    [Fact]
    public async Task ReplayTickingBlockReference()
    {
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        var cases = fixture.GetProperty("cases").EnumerateArray().ToArray();
        await FixtureCases.AssertAllAsync(cases, Name, ReplayAsync);

        output.WriteLine($"Replayed {cases.Length} ticking block cases.");
    }

    private static string Name(JsonElement probe) => probe.GetProperty("name").GetString()!;

    private static async Task ReplayAsync(JsonElement probe)
    {
        await using var context = TestServerFactory.CreateServer($"ticking-blocks-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";
        if (probe.TryGetProperty("entityDrops", out var entityDrops))
            world.LevelData.GameRules["entity_drops"] = entityDrops.GetBoolean() ? "true" : "false";

        await AddEmptyChunksAsync(world);

        // A player with the nine chunks, who gets the level events sent there.
        var listener = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Listener",
            new VectorD(8, 80, 8));
        world.Players.TryAdd(listener.Uuid, listener);
        foreach (var chunk in Chunks(world))
            listener.LoadedChunks.Add(NumericsHelper.IntsToLong(chunk.X, chunk.Z));

        var client = Assert.IsType<TestClient>(listener.Client);

        // The case's blocks, without updates, as vanilla's block view holds them.
        foreach (var placement in probe.GetProperty("setup").EnumerateArray())
        {
            var state = VanillaSulfurBlocks.ParseState(placement.GetProperty("state").GetString()!);
            world.BlockUpdates.SetBlock(Position(placement.GetProperty("pos")), state,
                BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        }

        var seed = probe.TryGetProperty("seed", out var given) ? given.GetInt64() : 0;
        var random = new RecordingRandom(seed);
        world.GrowthRandom = random;

        var differences = new List<string>();
        var origin = Position(probe.GetProperty("origin"));
        var call = probe.GetProperty("call").GetString();
        switch (call)
        {
            case "randomTick":
                RandomTick(differences, probe, world, origin, random);
                break;
            case "tick":
                ScheduledTick(differences, world, origin, random);
                break;
            case "set":
                var changed = VanillaSulfurBlocks.ParseState(probe.GetProperty("state").GetString()!);
                world.BlockUpdates.SetBlock(origin, changed);
                break;
            case "landing":
                await LandAsync(differences, probe, world, origin);
                break;
            default:
                throw new InvalidOperationException($"Unknown call {call}");
        }

        // Obsidian destroys blocks in the tick's entity phase, where vanilla's destroyBlock is immediate; the tick
        // runs it. The probe's view doesn't drop items, so the drops' draws come from another random.
        if (probe.GetProperty("destroyed").GetArrayLength() > 0)
        {
            world.GrowthRandom = new LegacyRandomSource(seed);
            await world.DoWorldTickAsync();
        }

        CompareDraws(differences, probe.GetProperty("draws"), random.Draws);
        CompareBlocks(differences, probe.GetProperty("after"), world);
        CompareTicks(differences, probe.GetProperty("scheduled"), world);
        CompareEvents(differences, probe, client.SentPackets.OfType<LevelEventPacket>().ToList());
        CompareFallingBlocks(differences, probe.GetProperty("entities"), world);

        if (differences.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", differences));
    }

    // AbstractLevel.TickChunkBlocks: only a randomly ticking state ticks, through its generated owner.
    private static void RandomTick(
        List<string> differences,
        JsonElement probe,
        WorldData.World world,
        Vector origin,
        RecordingRandom random)
    {
        var block = world.BlockUpdates.GetBlock(origin);
        var ticks = BlockPhysics.IsRandomlyTicking(block.GetHashCode());
        Expect(differences, "ticks", probe.GetProperty("ticks").GetBoolean(), ticks);
        if (!ticks)
            return;

        if (Growth.VanillaRandomTick(block) is not IRandomTickBehavior behavior)
        {
            differences.Add($"{block.UnlocalizedName} has no random tick owner");
            return;
        }

        behavior.RandomTick(world, origin, block, random);
    }

    // LevelBlockUpdates.RunScheduledTick's dispatch, with the case's random: the owner of the block's tick.
    private static void ScheduledTick(
        List<string> differences,
        WorldData.World world,
        Vector origin,
        RecordingRandom random)
    {
        var block = world.BlockUpdates.GetBlock(origin);
        if (BlocksRegistry.GetTickBehavior(block.RegistryId) is not IScheduledTickBehavior behavior)
        {
            differences.Add($"{block.UnlocalizedName} has no scheduled tick owner");
            return;
        }

        behavior.Tick(world.BlockUpdates, origin, block, random);
    }

    // A falling block of the case's state dropped onto the origin from a few blocks up, ticked until it lands, and
    // what it left there counted before an item can roll off the floor.
    private static async Task LandAsync(
        List<string> differences,
        JsonElement probe,
        WorldData.World world,
        Vector origin)
    {
        var falling = new FallingBlock((VectorD)origin + new VectorD(0.5, 3, 0.5))
        {
            Level = world,
            Block = VanillaSulfurBlocks.ParseState(probe.GetProperty("state").GetString()!),
            Type = EntityType.FallingBlock,
            EntityId = Server.GetNextEntityId(),
            Silent = probe.GetProperty("silent").GetBoolean()
        };
        Assert.True(world.TryAddEntity(falling));

        for (var tick = 0; tick < 60 && IsNear(world, origin, falling); tick++)
        {
            await world.DoWorldTickAsync();
        }

        var remaining = world.GetNonPlayerEntitiesInRange((VectorD)origin, 10).ToArray();
        Expect(differences, "removed", probe.GetProperty("removed").GetBoolean(), !remaining.Contains(falling));

        var items = remaining.OfType<ItemEntity>()
            .Where(item => !item.Removed)
            .Sum(item => item.Item.Count);
        Expect(differences, "items", probe.GetProperty("items").GetInt32(), items);
    }

    private static bool IsNear(WorldData.World world, Vector origin, Entity entity) =>
        world.GetNonPlayerEntitiesInRange((VectorD)origin, 10).Contains(entity);

    private static void CompareDraws(List<string> differences, JsonElement expected, List<Draw> actual)
    {
        var draws = expected.EnumerateArray().Select(Draw.Parse);
        ExpectSequence(differences, "draws", draws.Select(Text), actual.Select(Text));
    }

    private static string Text(Draw draw) => draw.ToString();

    private static void CompareBlocks(List<string> differences, JsonElement expected, WorldData.World world)
    {
        foreach (var placement in expected.EnumerateArray())
        {
            var position = Position(placement.GetProperty("pos"));
            var state = placement.GetProperty("state").GetString()!;
            var actual = world.BlockUpdates.GetBlock(position);
            if (!VanillaSulfurBlocks.ParseState(state).IsSameState(actual))
                differences.Add($"{position}: vanilla {state}, Obsidian {actual}");
        }
    }

    // Every block and fluid tick pending in the nine chunks, against those vanilla scheduled. Vanilla's list can
    // schedule the same tick twice, which is pending once.
    private static void CompareTicks(List<string> differences, JsonElement expected, WorldData.World world)
    {
        var vanilla = expected.EnumerateArray()
            .Select(tick => Tick(Position(tick.GetProperty("pos")), tick.GetProperty("type").GetString()!,
                tick.GetProperty("delay").GetInt32()))
            .Distinct()
            .ToList();

        var obsidian = new List<string>();
        foreach (var chunk in Chunks(world))
        {
            var blockTicks = chunk.BlockTicks.Pack();
            var fluidTicks = chunk.FluidTicks.Pack();
            obsidian.AddRange(blockTicks.Select(tick => Tick(tick.Position, tick.Block, tick.Delay)));
            obsidian.AddRange(fluidTicks.Select(tick => Tick(tick.Position, FluidName(tick.Type), tick.Delay)));
        }

        obsidian.Sort(StringComparer.Ordinal);
        vanilla.Sort(StringComparer.Ordinal);
        ExpectSequence(differences, "ticks", vanilla, obsidian);
    }

    private static string Tick(Vector position, string type, int delay) => $"{position} {type} in {delay}";

    // The level events clients got, in order, against vanilla's; a block destroyed is vanilla's destroy event there.
    private static void CompareEvents(List<string> differences, JsonElement probe, List<LevelEventPacket> packets)
    {
        var vanilla = probe.GetProperty("events").EnumerateArray()
            .Select(recorded => Event(recorded.GetProperty("type").GetInt32(), Position(recorded.GetProperty("pos")),
                recorded.GetProperty("data").GetInt32()))
            .ToList();
        var obsidian = packets
            .Where(packet => packet.Type != DestroyEvent)
            .Select(packet => Event(packet.Type, packet.Position, packet.Data))
            .ToList();
        ExpectSequence(differences, "events", vanilla, obsidian);

        var destroyed = probe.GetProperty("destroyed").EnumerateArray()
            .Select(block => Position(block.GetProperty("pos")).ToString())
            .ToList();
        var broken = packets
            .Where(packet => packet.Type == DestroyEvent)
            .Select(packet => packet.Position.ToString())
            .ToList();
        ExpectSequence(differences, "destroyed", destroyed, broken);
    }

    private static string Event(int type, Vector position, int data) => $"{type} at {position} ({data})";

    // The falling blocks spawned, top down, with their block, position and the damage a stalactite does per block
    // fallen.
    private static void CompareFallingBlocks(List<string> differences, JsonElement expected, WorldData.World world)
    {
        var vanilla = expected.EnumerateArray()
            .Select(entity =>
            {
                var position = entity.GetProperty("pos");
                var damage = entity.GetProperty("hurtEntities").GetBoolean()
                    ? entity.GetProperty("damagePerDistance").GetSingle()
                    : 0f;
                var state = VanillaSulfurBlocks.ParseState(entity.GetProperty("state").GetString()!);
                var x = position[0].GetDouble();
                var y = position[1].GetDouble();
                var z = position[2].GetDouble();
                return Falling(state, x, y, z, damage);
            })
            .ToList();

        var obsidian = world.GetNonPlayerEntitiesInRange(new VectorD(8, 8, 8), 32)
            .OfType<FallingBlock>()
            .OrderByDescending(falling => falling.Position.Y)
            .Select(falling => Falling(falling.Block, falling.Position.X, falling.Position.Y, falling.Position.Z,
                falling.StalactiteDamage))
            .ToList();

        ExpectSequence(differences, "falling", vanilla, obsidian);
    }

    private static string Falling(IBlock block, double x, double y, double z, float damage) =>
        string.Create(CultureInfo.InvariantCulture, $"{block.StateId()} at ({x}, {y}, {z}) hurting {damage}");

    private static string FluidName(FluidKind fluid) => fluid switch
    {
        FluidKind.Water => "minecraft:water",
        FluidKind.FlowingWater => "minecraft:flowing_water",
        FluidKind.Lava => "minecraft:lava",
        FluidKind.FlowingLava => "minecraft:flowing_lava",
        _ => fluid.ToString()
    };

    private static void ExpectSequence(
        List<string> differences,
        string what,
        IEnumerable<string> vanilla,
        IEnumerable<string> obsidian)
    {
        var expected = string.Join(", ", vanilla);
        var actual = string.Join(", ", obsidian);
        if (expected != actual)
            differences.Add($"{what}: vanilla [{expected}], Obsidian [{actual}]");
    }

    private static void Expect<T>(List<string> differences, string what, T vanilla, T obsidian)
    {
        if (!EqualityComparer<T>.Default.Equals(vanilla, obsidian))
            differences.Add($"{what}: vanilla {vanilla}, Obsidian {obsidian}");
    }

    private static IEnumerable<Chunk> Chunks(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                yield return (Chunk)world.GetLoadedChunk(chunkX, chunkZ)!;
        }
    }

    // The nine chunks around the cases' blocks, empty and complete, so writes and reads reach them.
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
            .GetManifestResourceStream("Obsidian.Tests.Assets.ticking_blocks.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    /// <summary>A draw as the probe records it: the call, its bound for <c>nextInt(bound)</c>, its result.</summary>
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
