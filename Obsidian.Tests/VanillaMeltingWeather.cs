using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Configuration;
using Obsidian.API.Registries;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>melting_weather.json</c>, captured from vanilla (see <c>docs/scenario-captures.md</c>): ice, frosted ice
/// and snow layers melting on random and scheduled ticks, frosted ice scheduling its tick when placed, and the chunk
/// tick's precipitation pass freezing water, settling snow and filling cauldrons. Each case's blocks, biome, weather,
/// time and the light vanilla computed for them go in; the blocks that changed, the ticks scheduled, the level random's
/// next draw and the position sequence come out.
/// </summary>
public sealed class VanillaMeltingWeather(ITestOutputHelper output)
{
    // How far around a single block's target its changes are compared, as in the probe.
    private const int Reach = 2;

    [Fact]
    public async Task ReplayMeltingAndWeatherReference()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Tests.Assets.melting_weather.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        await FixtureCases.AssertAllAsync(cases, probe => probe.GetProperty("name").GetString()!, ReplayAsync);
        output.WriteLine($"Replayed {cases.Length} melting and weather cases.");
    }

    private static async Task ReplayAsync(JsonElement probe)
    {
        await using var context = TestServerFactory.CreateServer($"melting-weather-{Guid.NewGuid():N}");
        var level = await PrepareAsync(context, probe);
        var actors = await PrepareActorsAsync(level, probe);
        var kind = probe.GetProperty("kind").GetString();
        var region = kind == "precipitation" ? PrecipitationRegion(probe) : TargetRegion(probe);
        var before = region.ToDictionary(position => position, level.BlockUpdates.GetBlock);

        // What vanilla's light engine computed is the input, so the replay doesn't depend on Obsidian's lighting.
        Assert.Equal(probe.GetProperty("skyDarken").GetInt32(), level.SkyDarken());

        level.GrowthRandom.SetSeed(probe.GetProperty("seed").GetInt64());
        if (kind == "precipitation")
            RunPrecipitation(context, level, probe);
        else
            RunBlock(level, probe, kind!);

        var failures = new List<string>();
        if (probe.TryGetProperty("biomeSamples", out var samples))
        {
            foreach (var sample in samples.EnumerateArray())
            {
                var position = Position(sample.GetProperty("pos"));
                var chunk = level.GetLoadedChunk(position.X >> 4, position.Z >> 4)!;
                var actual = level.BiomeAt(chunk, position.X, position.Y, position.Z);
                if (actual.Name != sample.GetProperty("biome").GetString())
                {
                    failures.Add($"Biome at {position}: expected {sample.GetProperty("biome")}, "
                        + $"got {actual.Name}");
                }
            }
        }

        if (probe.TryGetProperty("entityPositions", out var positions))
        {
            for (var index = 0; index < actors.Count; index++)
            {
                var expected = EntityPosition(positions[index]);
                if (actors[index].Position != expected)
                    failures.Add($"Entity {index}: expected {expected}, got {actors[index].Position}");
            }
        }

        CompareChanges(probe, level, before, failures);
        CompareTicks(probe, "blockTicks", BlockTicks(level), failures);
        CompareTicks(probe, "fluidTicks", FluidTicks(level), failures);
        Compare(failures, "next random", probe.GetProperty("nextRandom").GetInt32(), level.GrowthRandom.NextInt());
        if (probe.TryGetProperty("randValueAfter", out var randValue))
            Compare(failures, "position sequence", randValue.GetInt32(), level.RandomTickValue);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // The case's level with the nine chunks around chunk (0, 0) in its biome and holding its blocks, then the weather,
    // time and game rules it ran with and the light vanilla had.
    private static async Task<AbstractLevel> PrepareAsync(RealServerContext context, JsonElement probe)
    {
        var world = context.World;
        var level = Dimension(probe) == "nether" ? AddNether(context) : (AbstractLevel)world;
        var biome = CodecRegistry.TryGetBiome(probe.GetProperty("biome").GetString()!, out var found)
            ? found
            : throw new InvalidOperationException("Unknown biome.");

        var rules = world.LevelData.GameRules;
        rules["spawn_mobs"] = "false";
        rules["random_tick_speed"] = probe.TryGetProperty("speed", out var speed) ? speed.GetInt32().ToString() : "0";
        rules["max_snow_accumulation_height"] = probe.TryGetProperty("maxSnowAccumulationHeight", out var max)
            ? max.GetInt32().ToString()
            : "1";

        world.LevelData.Raining = probe.TryGetProperty("raining", out var raining) && raining.GetBoolean();
        world.LevelData.Thundering = false;
        world.LevelData.DayTime = probe.TryGetProperty("dayTime", out var dayTime) ? dayTime.GetInt32() : 6000;

        if (probe.TryGetProperty("biomeSeed", out var biomeSeed))
            level.LevelData.RandomSeed = biomeSeed.GetInt64();

        if (probe.TryGetProperty("generator", out var generator) && generator.GetString() == "flat")
        {
            var settings = new FlatLevelSettings([], biome);
            level.Generator = new FlatStem("minecraft:overworld", "minecraft:overworld", settings).CreateGenerator();
        }

        var setup = Setup(probe);
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ, level.MinY, level.Height, ChunkGenStage.full);
                FillBiome(chunk, biome, probe);
                foreach (var (position, state) in setup)
                {
                    if (position.X >> 4 == chunkX && position.Z >> 4 == chunkZ)
                        chunk.SetBlock(position.X, position.Y, position.Z, state);
                }

                level.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
            }
        }

        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                await level.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
        }

        SetLight(level, probe);
        return level;
    }

    // The case's blocks: its setup, or its layers over the nine chunks.
    private static List<(Vector Position, IBlock State)> Setup(JsonElement probe)
    {
        var blocks = new List<(Vector, IBlock)>();
        if (probe.TryGetProperty("setup", out var setup))
        {
            foreach (var placement in setup.EnumerateArray())
                blocks.Add((Position(placement.GetProperty("pos")), Parse(placement.GetProperty("state"))));

            return blocks;
        }

        foreach (var layer in probe.GetProperty("layers").EnumerateArray())
        {
            var y = layer.GetProperty("y").GetInt32();
            for (var x = -16; x < 32; x++)
            {
                for (var z = -16; z < 32; z++)
                    blocks.Add((new Vector(x, y, z), Parse(LayerState(layer, x, z))));
            }
        }

        return blocks;
    }

    // A layer's state at a column: one state, or the tile's symbol at z modulo the rows and x modulo the row's length.
    private static JsonElement LayerState(JsonElement layer, int x, int z)
    {
        if (layer.TryGetProperty("state", out var state))
            return state;

        var tile = layer.GetProperty("tile").EnumerateArray().Select(row => row.GetString()!).ToArray();
        var row = tile[Mod(z, tile.Length)];
        var symbol = row[Mod(x, row.Length)].ToString();
        return layer.GetProperty("palette").GetProperty(symbol);
    }

    private static void SetLight(AbstractLevel level, JsonElement probe)
    {
        if (probe.GetProperty("kind").GetString() != "precipitation")
        {
            foreach (var light in probe.GetProperty("light").EnumerateArray())
            {
                var position = Position(light.GetProperty("pos"));
                SetLight(level, position, light.GetProperty("block").GetInt32(), light.GetProperty("sky").GetInt32());
            }

            return;
        }

        foreach (var light in probe.GetProperty("light").EnumerateArray())
        {
            var y = light.GetProperty("y").GetInt32();
            var block = light.GetProperty("block").GetString()!;
            var sky = light.GetProperty("sky").GetString()!;
            for (var index = 0; index < 256; index++)
            {
                var position = new Vector(index & 15, y, index >> 4);
                SetLight(level, position, Hex(block[index]), Hex(sky[index]));
            }
        }
    }

    private static void SetLight(AbstractLevel level, Vector position, int block, int sky)
    {
        var chunk = level.GetLoadedChunk(position.X >> 4, position.Z >> 4)!;
        chunk.SetLightLevel(position.X, position.Y, position.Z, LightType.Block, block);
        chunk.SetLightLevel(position.X, position.Y, position.Z, LightType.Sky, sky);
    }

    // A random tick, a scheduled tick or a placement at the target, through the paths the level's ticks take.
    private static void RunBlock(AbstractLevel level, JsonElement probe, string kind)
    {
        var target = Position(probe.GetProperty("target"));
        var blocks = level.BlockUpdates;
        var state = blocks.GetBlock(target);

        switch (kind)
        {
            case "random_tick":
                var behavior = Growth.VanillaRandomTick(state);
                Assert.NotNull(behavior);
                behavior.RandomTick(level, target, state, level.GrowthRandom);
                break;
            case "tick":
                blocks.RunScheduledTick(target, state.UnlocalizedName);
                break;
            case "place":
                blocks.SetBlock(target, Parse(probe.GetProperty("place")));
                break;
            default:
                throw new InvalidOperationException($"Unknown kind {kind}.");
        }
    }

    // The level's random ticks with a player in chunk (0, 0): only that chunk has all eight neighbours loaded, so the
    // chunk tick runs on it alone, as the probe ran it.
    private static void RunPrecipitation(RealServerContext context, AbstractLevel level, JsonElement probe)
    {
        var world = context.World;
        level.RandomTickValue = probe.GetProperty("randValue").GetInt32();

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Watcher", new VectorD(8, 250, 8));
        player.GameMode = GameMode.Survival;
        player.Level = level;
        Assert.True(level.TryAddPlayer(player));
        level.SnapshotPlayers();

        var ticks = probe.GetProperty("ticks").GetInt32();
        for (var tick = 0; tick < ticks; tick++)
            level.TickRandomBlocks();
    }

    private static async Task<List<Entity>> PrepareActorsAsync(AbstractLevel level, JsonElement probe)
    {
        var actors = new List<Entity>();
        if (!probe.TryGetProperty("entities", out var entities))
            return actors;

        foreach (var input in entities.EnumerateArray())
        {
            var actor = new ArmorStand
            {
                EntityId = 1000 + actors.Count,
                Level = level,
                Type = EntityType.ArmorStand
            };
            await actor.UpdatePositionAsync(EntityPosition(input.GetProperty("pos")), MovementFlags.OnGround);
            Assert.True(level.TryAddEntity(actor));
            actors.Add(actor);
        }

        return actors;
    }

    // Client correction is Obsidian-specific; the amount comes from the native entity capture used by the replay.
    [Fact]
    public async Task SnowLiftCorrectsConnectedPlayerAndObserver()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Tests.Assets.melting_weather.json")!;
        using var document = JsonDocument.Parse(stream);
        var probe = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(row => row.GetProperty("name").GetString() == "review_snow_lifts_entities_2_limit_8");
        await using var context = TestServerFactory.CreateServer($"snow-lift-packets-{Guid.NewGuid():N}");
        var level = await PrepareAsync(context, probe);
        var initial = EntityPosition(probe.GetProperty("entities")[0].GetProperty("pos"));
        var expected = EntityPosition(probe.GetProperty("entityPositions")[0]);
        var standing = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Standing", initial);
        var observer = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Observer",
            initial + new VectorD(2, 1, 0));
        await standing.UpdatePositionAsync(initial, MovementFlags.OnGround);
        Assert.True(level.TryAddPlayer(standing));
        Assert.True(level.TryAddPlayer(observer));
        var motion = new VectorD(0.1, -0.2, 0.3);
        standing.Motion = motion;

        // A lift replaces an unconfirmed plugin correction using the owner's next id, not another random draw.
        var client = (TestClient)standing.Client;
        var previous = new PlayerPositionPacket { TeleportId = 999, Position = initial, Delta = motion };
        standing.TeleportId = previous.TeleportId;
        await client.QueuePacketAsync(previous);
        client.ClearPackets();

        level.GrowthRandom.SetSeed(probe.GetProperty("seed").GetInt64());
        RunPrecipitation(context, level, probe);

        Assert.Equal(expected, standing.Position);
        Assert.Equal(expected.Y, standing.BoundingBox.Min.Y);
        Assert.Equal(motion, standing.Motion);
        var correction = Assert.Single(client.SentPackets.OfType<PlayerPositionPacket>());
        Assert.Equal(expected, correction.Position);
        Assert.Equal(motion, correction.Delta);
        Assert.Equal(1000, correction.TeleportId);
        Assert.Equal(standing.TeleportId, correction.TeleportId);
        var watching = (TestClient)observer.Client;
        var seen = watching.SentPackets.OfType<EntityPositionSyncPacket>()
            .Where(packet => packet.EntityId == standing.EntityId);
        Assert.Equal(expected, Assert.Single(seen).Position);

        // A move sent before the client saw the lift cannot undo it or reach the observer.
        watching.ClearPackets();
        await client.ConfirmTeleportAsync(previous);
        using var buffer = new NetworkBuffer();
        VectorD.Write(initial, buffer);
        buffer.WriteByte((byte)MovementFlags.OnGround);
        buffer.Offset = 0;
        var stale = new MovePlayerPosPacket();
        stale.Populate(buffer);
        await stale.HandleAsync(context.Server, standing);

        Assert.Equal(expected, standing.Position);
        Assert.Equal(expected.Y, standing.BoundingBox.Min.Y);
        Assert.DoesNotContain(watching.SentPackets,
            packet => packet is MoveEntityPosPacket or MoveEntityPosRotPacket or EntityPositionSyncPacket);

        await client.ConfirmTeleportAsync(correction);
        await stale.HandleAsync(context.Server, standing);
        Assert.Equal(initial, standing.Position);
    }

    private static void CompareChanges(
        JsonElement probe,
        AbstractLevel level,
        Dictionary<Vector, IBlock> before,
        List<string> failures)
    {
        var expected = new Dictionary<Vector, IBlock>();
        foreach (var change in probe.GetProperty("changes").EnumerateArray())
        {
            var text = change.GetString()!;
            var separator = text.IndexOf('=');
            var coordinates = text[..separator].Split(',').Select(int.Parse).ToArray();
            expected[new Vector(coordinates[0], coordinates[1], coordinates[2])] = VanillaSulfurBlocks.ParseState(text[(separator + 1)..]);
        }

        foreach (var (position, old) in before)
        {
            var now = level.BlockUpdates.GetBlock(position);
            var changed = !now.IsSameState(old);
            if (expected.TryGetValue(position, out var wanted))
            {
                if (!now.IsSameState(wanted))
                    failures.Add($"{position}: expected {Format(wanted)}, got {Format(now)}");
            }
            else if (changed)
            {
                failures.Add($"{position}: expected {Format(old)} unchanged, got {Format(now)}");
            }
        }
    }

    private static void CompareTicks(JsonElement probe, string property, IEnumerable<string> actual, List<string> failures)
    {
        var expected = probe.GetProperty(property).EnumerateArray().Select(tick => tick.GetString()!).Order().ToArray();
        var got = actual.Order().ToArray();
        if (!expected.SequenceEqual(got))
            failures.Add($"{property}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", got)}]");
    }

    private static void Compare(List<string> failures, string what, int expected, int actual)
    {
        if (expected != actual)
            failures.Add($"{what}: expected {expected}, got {actual}");
    }

    // The ticks scheduled in the nine chunks, as "x,y,z=type@delay" like the probe records them.
    private static IEnumerable<string> BlockTicks(AbstractLevel level)
    {
        foreach (var chunk in AreaChunks(level))
        {
            foreach (var tick in chunk.BlockTicks.Pack())
                yield return $"{Key(tick.Position)}={tick.Block}@{tick.Delay}";
        }
    }

    private static IEnumerable<string> FluidTicks(AbstractLevel level)
    {
        foreach (var chunk in AreaChunks(level))
        {
            foreach (var tick in chunk.FluidTicks.Pack())
                yield return $"{Key(tick.Position)}={FluidName(tick.Type)}@{tick.Delay}";
        }
    }

    private static string FluidName(FluidKind fluid) => fluid switch
    {
        FluidKind.Water => "minecraft:water",
        FluidKind.FlowingWater => "minecraft:flowing_water",
        FluidKind.Lava => "minecraft:lava",
        FluidKind.FlowingLava => "minecraft:flowing_lava",
        _ => fluid.ToString()
    };

    private static IEnumerable<Chunk> AreaChunks(AbstractLevel level)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                yield return (Chunk)level.GetLoadedChunk(chunkX, chunkZ)!;
        }
    }

    private static List<Vector> TargetRegion(JsonElement probe)
    {
        var target = Position(probe.GetProperty("target"));
        var positions = new List<Vector>();
        for (var dx = -Reach; dx <= Reach; dx++)
        {
            for (var dy = -Reach; dy <= Reach; dy++)
            {
                for (var dz = -Reach; dz <= Reach; dz++)
                    positions.Add(target + new Vector(dx, dy, dz));
            }
        }

        return positions;
    }

    // Chunk (0, 0) from just below the layers to the snow that can grow on them, as in the probe.
    private static List<Vector> PrecipitationRegion(JsonElement probe)
    {
        var heights = probe.GetProperty("layers").EnumerateArray().Select(layer => layer.GetProperty("y").GetInt32()).ToArray();
        var positions = new List<Vector>();
        for (var y = heights.Min() - 1; y <= heights.Max() + 3; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                for (var z = 0; z < 16; z++)
                    positions.Add(new Vector(x, y, z));
            }
        }

        return positions;
    }

    private static void FillBiome(Chunk chunk, API.Registry.Codecs.Biomes.BiomeCodec biome, JsonElement probe)
    {
        var hasBorder = probe.TryGetProperty("biomeBorder", out var border);
        var other = hasBorder && CodecRegistry.TryGetBiome(border.GetProperty("other").GetString()!, out var found)
            ? found : biome;
        for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y += 4)
        {
            for (var x = 0; x < 16; x += 4)
            {
                for (var z = 0; z < 16; z += 4)
                {
                    var selected = biome;
                    if (hasBorder)
                    {
                        var coordinate = border.GetProperty("axis").GetString() switch
                        {
                            "x" => chunk.X * 4 + (x >> 2),
                            "y" => y >> 2,
                            "z" => chunk.Z * 4 + (z >> 2),
                            _ => throw new InvalidOperationException("Unknown biome border axis.")
                        };
                        if (coordinate >= border.GetProperty("quart").GetInt32())
                            selected = other;
                    }

                    chunk.SetBiome(x, y, z, selected);
                }
            }
        }
    }

    private static WorldData.Dimension AddNether(RealServerContext context)
    {
        var world = context.World;
        var nether = new WorldData.Dimension(
            NullLogger<WorldData.Dimension>.Instance,
            context.Broadcaster,
            new TestOptionsMonitor<ServerConfiguration>(world.Configuration),
            context.EventDispatcher,
            new EmptyWorldGenerator(),
            "minecraft:the_nether",
            world);
        nether.Initialize(CodecRegistry.Dimensions.All["minecraft:the_nether"]);
        world.RegisterDimension(nether);
        return nether;
    }

    private static string Dimension(JsonElement probe) =>
        probe.TryGetProperty("dimension", out var dimension) ? dimension.GetString()! : "overworld";

    private static IBlock Parse(JsonElement state) => VanillaSulfurBlocks.ParseState(state.GetString()!);

    private static string Format(IBlock state) =>
        state.UnlocalizedName + "["
        + string.Join(',', state.GetProperties().Select(pair => $"{pair.Key}={pair.Value}")) + "]";

    private static VectorD EntityPosition(JsonElement array) =>
        new(array[0].GetDouble(), array[1].GetDouble(), array[2].GetDouble());

    private static Vector Position(JsonElement array) =>
        new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    private static string Key(Vector position) => $"{position.X},{position.Y},{position.Z}";

    private static int Hex(char digit) => Convert.ToInt32(digit.ToString(), 16);

    private static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
