using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Entities touching blocks, against <c>Assets/block_contact_scenarios.json</c>: pressure plates, tripwires,
/// farmland, eggs, magma and soft landing surfaces, the blocks that slow entities or hold them (soul sand, honey,
/// cobwebs, berry bushes, powder snow, scaffolding, slime), and players' posed boxes. Items, armor stands, mobs and
/// players are dropped, pushed, walked, jumped, removed and killed. References come from headless vanilla servers
/// (<c>ScenarioContactCaptures.cs</c> and <c>Java/BlockContactFixtures.java</c>), using
/// <c>dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=block_contact</c>.
/// </summary>
/// <remarks>
/// Every scenario replays on one shared test server, each in its own box, with a player high above it so that its
/// chunks tick. Its entity events run before the tick they belong to, as the capture runs them between ticks, and
/// each checkpoint compares the box's blocks and where each entity is.
/// </remarks>
// The server merges plugin tags into vanilla's, as PluginTagFiles does.
[Collection(nameof(PluginTagFiles))]
public partial class VanillaBlockContact
{
    private const string Symbols = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // How far an entity may be from where vanilla had it, which the capture prints to the last digit.
    private const double PositionTolerance = 1e-6;

    // Chunks loaded on each side of the box's chunks, so that the box's chunks tick as a player's do.
    private const int ChunkRing = 2;

    [Fact(DisplayName = "Block contacts match vanilla movement, effects and ordered writes")]
    public async Task ScenariosMatchVanilla()
    {
        var document = LoadFixture();
        Assert.Equal(ServerConstants.ProtocolDescription, document.GetProperty("version").GetString());

        var scenarios = document.GetProperty("scenarios").EnumerateArray().ToArray();
        await using var replay = new Replay();
        await FixtureCases.AssertAllAsync(
            scenarios,
            scenario => scenario.GetProperty("name").GetString()!,
            async scenario =>
            {
                var differences = await replay.RunAsync(scenario);
                Assert.True(differences.Count == 0, string.Join("\n", differences.Take(30)));
            });
    }

    // Force the baby draw so accidental natural-spawn finalization fails this NBT-summon replay every time.
    private sealed class BabySpawnRandom : Random
    {
        public override float NextSingle() => 0;
    }

    private static JsonElement LoadFixture()
    {
        const string resource = "Obsidian.Tests.Assets.block_contact_scenarios.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
        return JsonDocument.Parse(stream).RootElement.Clone();
    }

    /// <summary>One test server whose world every scenario replays in, each in its own box.</summary>
    private sealed partial class Replay : IAsyncDisposable
    {
        private readonly RealServerContext context = TestServerFactory.CreateServer($"contact-{Guid.NewGuid():N}");
        private readonly HashSet<(int X, int Z)> loaded = [];
        private Obsidian.Entities.Player contactObserver;

        public async Task<List<string>> RunAsync(JsonElement scenario)
        {
            var min = ReadVector(scenario.GetProperty("min"));
            var max = ReadVector(scenario.GetProperty("max"));
            await this.PrepareAsync(min, max);
            if (scenario.TryGetProperty("contactMoves", out _))
                return await this.RunContactMovesAsync(scenario);


            var world = this.context.World;
            foreach (var placement in scenario.GetProperty("setup").EnumerateArray())
            {
                var position = ReadVector(placement.GetProperty("pos"));
                var state = ParseState(placement.GetProperty("state").GetString()!);
                world.PrepareObserverCommandPlacement(position, state);
                await world.SetBlockAsync(position, state, doBlockUpdate: true);
            }

            var events = scenario.GetProperty("entities").EnumerateArray().ToArray();
            var checkpoints = scenario.GetProperty("checkpoints").EnumerateArray().ToArray();
            var entities = new Dictionary<string, Entity>();
            var differences = await CompareAsync(world, checkpoints[0], min, max, entities);

            for (var tick = 1; tick < checkpoints.Length; tick++)
            {
                foreach (var entry in events.Where(entry => entry.GetProperty("tick").GetInt32() == tick))
                    await RunEventAsync(world, entry, entities);

                await world.DoWorldTickAsync();
                differences.AddRange(await CompareAsync(world, checkpoints[tick], min, max, entities));
            }

            foreach (var entity in entities.Values.Where(entity => entity.IsMemberOf(world)))
                await world.DestroyEntityAsync(entity);

            return differences;
        }

        public ValueTask DisposeAsync() => this.context.DisposeAsync();

        // Empty chunks around the box, kept across scenarios, and a player far above it so that it ticks.
        private async Task PrepareAsync(Vector min, Vector max)
        {
            var world = this.context.World;
            var fromX = (min.X >> 4) - ChunkRing;
            var toX = (max.X >> 4) + ChunkRing;
            var fromZ = (min.Z >> 4) - ChunkRing;
            var toZ = (max.Z >> 4) + ChunkRing;
            for (var chunkX = fromX; chunkX <= toX; chunkX++)
            {
                for (var chunkZ = fromZ; chunkZ <= toZ; chunkZ++)
                {
                    if (this.loaded.Add((chunkX, chunkZ)))
                        WorldBlockEntities.AddEmptyChunk(world, chunkX, chunkZ);
                }
            }

            for (var chunkX = fromX; chunkX <= toX; chunkX++)
            {
                for (var chunkZ = fromZ; chunkZ <= toZ; chunkZ++)
                    await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
            }

            var above = new VectorD((min.X + max.X) / 2.0, max.Y + 12, (min.Z + max.Z) / 2.0);
            var name = $"Watcher{world.Players.Count}";
            var player = TestServerFactory.CreateConnectedPlayer(this.context.Server, world, name, above);
            await TestServerFactory.JoinConnectedPlayerAsync(this.context.Server, player);
            player.LoadedChunks.Add(0);
            this.contactObserver = player;
        }
    }

    // Runs an entity event as the capture's command did: summon, teleport, remove at once, or kill in place.
    private static async Task RunEventAsync(WorldData.World world, JsonElement entry, Dictionary<string, Entity> entities)
    {
        if (entry.TryGetProperty("summon", out var summon))
        {
            entities[summon.GetString()!] = Summon(world, entry);
            return;
        }

        if (entry.TryGetProperty("move", out var move))
        {
            var entity = entities[move.GetString()!];
            await entity.TeleportAsync(ReadLocation(entry.GetProperty("pos")));
            return;
        }

        if (entry.TryGetProperty("remove", out var remove))
        {
            await world.DestroyEntityAsync(entities[remove.GetString()!]);
            return;
        }

        var killed = Assert.IsAssignableFrom<Living>(entities[entry.GetProperty("kill").GetString()!]);
        await killed.KillAsync(killed);
    }

    // Like /summon with the event's data: an item's stack, a mob without AI, no gravity, a starting velocity.
    private static Entity Summon(WorldData.World world, JsonElement entry)
    {
        var type = Enum.Parse<EntityType>(
            entry.GetProperty("type").GetString()!["minecraft:".Length..].Replace("_", ""),
            ignoreCase: true);
        // The spawner's factory has no case for these, which /summon then makes as plain entities.
        Entity entity = type switch
        {
            EntityType.Item => new ItemEntity { Level = world },
            EntityType.Husk => new Husk { Level = world, Random = new BabySpawnRandom() },
            EntityType.ArmorStand => new ArmorStand { Level = world },
            EntityType.AreaEffectCloud => new AreaEffectCloud { Level = world },
            _ => EntitySpawner.Create(type, world),
        };
        entity.Type = type;
        entity.EntityId = Obsidian.Server.GetNextEntityId();
        entity.Position = ReadLocation(entry.GetProperty("pos"));
        if (entry.TryGetProperty("motion", out var motion))
            entity.Motion = ReadLocation(motion);

        if (entry.TryGetProperty("noGravity", out var noGravity))
            entity.NoGravity = noGravity.GetBoolean();

        if (entity is ItemEntity item)
            item.Item = new ItemStack(ItemsRegistry.Get(entry.GetProperty("item").GetString()!), 1);

        if (entity is Mob mob && entry.TryGetProperty("noAi", out var noAi) && noAi.GetBoolean())
            mob.MobBitMask |= MobBitmask.NoAi;

        // /summon with the capture's NBT skips natural-spawn age, equipment and attribute randomization.
        if (entity is Mob summonedMob)
            summonedMob.InitializeAi(finalizeSpawn: false);

        world.SpawnEntity(entity);
        return entity;
    }

    private static async Task<List<string>> CompareAsync(
        WorldData.World world,
        JsonElement checkpoint,
        Vector min,
        Vector max,
        Dictionary<string, Entity> entities)
    {
        var differences = new List<string>();
        var tick = checkpoint.GetProperty("tick").GetInt32();
        var palette = checkpoint.GetProperty("palette").EnumerateArray().Select(state => state.GetString()!).ToArray();
        var rows = checkpoint.GetProperty("rows").EnumerateArray().Select(row => row.GetString()!).ToArray();
        var depth = max.Z - min.Z + 1;
        for (var y = min.Y; y <= max.Y; y++)
        {
            for (var z = min.Z; z <= max.Z; z++)
            {
                var row = rows[(y - min.Y) * depth + (z - min.Z)];
                for (var x = min.X; x <= max.X; x++)
                {
                    var expected = palette[Symbols.IndexOf(row[x - min.X])];
                    var actual = await world.GetBlockAsync(new Vector(x, y, z));
                    if (actual is not null && actual.IsSameState(ParseState(expected)))
                        continue;

                    differences.Add($"tick {tick} ({x},{y},{z}): vanilla {expected}, ours {Describe(actual, expected)}");
                }
            }
        }

        // Vanilla's selector lists the entities still alive in the level; a dying mob is no longer one.
        var expectedEntities = checkpoint.GetProperty("entities");
        foreach (var (tag, entity) in entities)
        {
            var present = entity.IsMemberOf(world) && entity is not Living { Alive: false };
            if (!expectedEntities.TryGetProperty(tag, out var position))
            {
                if (present)
                    differences.Add($"tick {tick} {tag}: vanilla's is gone, ours is at {Format(entity.Position)}");

                continue;
            }

            var vanilla = ReadLocation(position);
            if (!present)
                differences.Add($"tick {tick} {tag}: vanilla's is at {Format(vanilla)}, ours is gone");
            else if (VectorD.Distance(vanilla, entity.Position) > PositionTolerance)
                differences.Add($"tick {tick} {tag}: vanilla's is at {Format(vanilla)}, ours at {Format(entity.Position)}");
        }

        return differences;
    }

    private static string Format(VectorD position) => string.Create(
        CultureInfo.InvariantCulture,
        $"({position.X:0.#####}, {position.Y:0.#####}, {position.Z:0.#####})");

    // The block's state with the same properties, in the order the vanilla state text lists them.
    private static string Describe(IBlock? block, string vanillaState)
    {
        if (block is null)
            return "nothing loaded";

        var bracket = vanillaState.IndexOf('[');
        if (bracket < 0)
            return block.UnlocalizedName;

        var keys = vanillaState[(bracket + 1)..^1].Split(',').Select(property => property.Split('=')[0]);
        var properties = keys.Select(key => $"{key}={block.GetProperty(key)}");
        return $"{block.UnlocalizedName}[{string.Join(",", properties)}]";
    }

    private static IBlock ParseState(string state)
    {
        var bracket = state.IndexOf('[');
        if (bracket < 0)
            return BlockStateProperties.GetState(state, new Dictionary<string, string>());

        var properties = state[(bracket + 1)..^1]
            .Split(',')
            .Select(property => property.Split('='))
            .ToDictionary(pair => pair[0], pair => pair[1]);
        return BlockStateProperties.GetState(state[..bracket], properties);
    }

    private static Vector ReadVector(JsonElement array) =>
        new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    private static VectorD ReadLocation(JsonElement array) =>
        new(array[0].GetDouble(), array[1].GetDouble(), array[2].GetDouble());
}
