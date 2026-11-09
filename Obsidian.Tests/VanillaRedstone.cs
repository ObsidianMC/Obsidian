using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Redstone;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Redstone parity fixtures. Each scenario in <c>Assets/redstone_scenarios.json</c> was recorded from a headless
/// vanilla server by the asset generator's scenario captures (<c>Obsidian.AssetGenerator/ScenarioCaptures.cs</c>,
/// <c>dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios</c>): the server's game time is frozen and
/// advanced one tick at a time, and every checkpoint holds the block states of the scenario's box.
/// </summary>
/// <remarks>
/// Each scenario is validated for completeness and known block states, then replayed on the live directional power
/// engine with <see cref="RedstoneReplay"/> using the timing the fixture documents. Scenarios marked <c>level</c> need
/// block entities (containers and hoppers), so they replay on a whole level of a test server instead.
/// </remarks>
// The level scenarios start servers, which merge plugin tags into vanilla's, as PluginTagFiles does.
[Collection(nameof(PluginTagFiles))]
public class VanillaRedstone
{
    private const string Symbols = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>The categories the fixture must cover.</summary>
    private static readonly string[] RequiredCoverage =
    [
        "source_timing", "dust_attenuation", "repeater_delay", "repeater_lock", "comparator_modes", "observer_pulses",
        "torch_inversion", "torch_burnout", "save_reload", "observer_containers", "observer_shape_updates",
        "container_comparators",
    ];

    [Fact]
    public async Task ScenariosMatchVanilla()
    {
        var covered = Fixture.Shared.Scenarios.Values
            .SelectMany(scenario => scenario.GetProperty("coverage").EnumerateArray().Select(tag => tag.GetString()!))
            .ToHashSet();

        Assert.Equal(ServerConstants.ProtocolDescription, Fixture.Shared.Document.GetProperty("version").GetString());
        Assert.Empty(RequiredCoverage.Where(tag => !covered.Contains(tag)));
        await using var levelWorld = new LevelScenarioWorld();
        await FixtureCases.AssertAllAsync(
            Fixture.Shared.Scenarios,
            scenario => scenario.Key,
            scenario => AssertScenarioMatchesVanillaAsync(scenario.Key, scenario.Value, levelWorld));
    }

    private static async Task AssertScenarioMatchesVanillaAsync(
        string name, JsonElement scenario, LevelScenarioWorld levelWorld)
    {
        AssertScenarioIsWellFormed(name, scenario);

        IRedstoneScenarioWorld world;
        if (scenario.TryGetProperty("level", out var onLevel) && onLevel.GetBoolean())
        {
            await levelWorld.PrepareAsync(scenario);
            world = levelWorld;
        }
        else
        {
            world = new EngineScenarioWorld(scenario);
        }

        var diffs = await RedstoneReplay.CompareAsync(world, scenario);
        Assert.True(diffs.Count == 0, $"{diffs.Count} blocks differ from vanilla:\n{string.Join("\n", diffs.Take(20))}");
    }

    private static void AssertScenarioIsWellFormed(string name, JsonElement scenario)
    {
        var (min, max) = (ReadVector(scenario.GetProperty("min")), ReadVector(scenario.GetProperty("max")));
        var ticks = scenario.GetProperty("checkpoints").GetArrayLength() - 1;
        var width = max.X - min.X + 1;
        var height = max.Y - min.Y + 1;
        var depth = max.Z - min.Z + 1;

        // One checkpoint per tick from 0 to the scenario's last tick, in order.
        var checkpoints = scenario.GetProperty("checkpoints").EnumerateArray().ToArray();
        for (var index = 0; index < checkpoints.Length; index++)
            Assert.Equal(index, checkpoints[index].GetProperty("tick").GetInt32());

        foreach (var checkpoint in checkpoints)
        {
            var palette = checkpoint.GetProperty("palette").EnumerateArray().Select(state => state.GetString()!).ToArray();
            var rows = checkpoint.GetProperty("rows").EnumerateArray().Select(row => row.GetString()!).ToArray();
            var tick = checkpoint.GetProperty("tick").GetInt32();

            Assert.Equal(height * depth, rows.Length);
            foreach (var row in rows)
                Assert.Equal(width, row.Length);

            // Every symbol indexes the palette, and every palette entry is a state Obsidian knows, with the properties it names.
            Assert.NotEmpty(palette);
            Assert.All(rows.SelectMany(row => row), symbol =>
            {
                var symbolIndex = Symbols.IndexOf(symbol);
                Assert.True(symbolIndex >= 0 && symbolIndex < palette.Length,
                    $"{name} tick {tick}: symbol '{symbol}' has no palette entry");
            });
            foreach (var state in palette)
                AssertStateRoundTrips(state, $"{name} tick {tick}");

            Assert.True(
                checkpoint.TryGetProperty("items", out _) == scenario.TryGetProperty("item", out _),
                $"{name} tick {tick}: item counts must be recorded exactly when the scenario names an item");
        }

        foreach (var action in scenario.GetProperty("actions").EnumerateArray())
        {
            var actionTick = action.GetProperty("tick").GetInt32();
            Assert.InRange(actionTick, 1, ticks);
            if (action.TryGetProperty("state", out var state))
            {
                AssertStateRoundTrips(state.GetString()!, $"{name} action at tick {actionTick}");
                var position = ReadVector(action.GetProperty("pos"));
                Assert.True(IsInside(position, min, max), $"{name}: action at tick {actionTick} is outside the box");
            }
            else
            {
                Assert.True(action.GetProperty("reload").GetBoolean());
                Assert.True(action.GetProperty("ticks").GetInt32() >= 0);
            }
        }

        if (scenario.TryGetProperty("arrows", out var arrowShots))
        {
            foreach (var arrow in arrowShots.EnumerateArray())
            {
                var arrowTick = arrow.GetProperty("tick").GetInt32();
                Assert.InRange(arrowTick, 1, ticks);
                Assert.True(IsInside(ReadVector(arrow.GetProperty("block")), min, max), $"{name}: arrow block at tick {arrowTick} is outside the box");
            }
        }

        if (scenario.TryGetProperty("entities", out var entityEvents))
        {
            foreach (var entity in entityEvents.EnumerateArray())
            {
                var entityTick = entity.GetProperty("tick").GetInt32();
                Assert.InRange(entityTick, 1, ticks);
                foreach (var cell in entity.GetProperty("contacts").EnumerateArray())
                    Assert.True(IsInside(ReadVector(cell), min, max), $"{name}: contact at tick {entityTick} is outside the box");
            }
        }
    }

    /// <summary>
    /// The replay's world on the live pipeline and the directional engine: real chunks loaded over the scenario's box, and setblock
    /// through the same path as a placement.
    /// </summary>
    private sealed class EngineScenarioWorld : IRedstoneScenarioWorld
    {
        private readonly RedstoneTestWorld world = new();

        public EngineScenarioWorld(JsonElement scenario)
        {
            var (min, max) = (ReadVector(scenario.GetProperty("min")), ReadVector(scenario.GetProperty("max")));
            // A ring of chunks around the box, so its chunks tick the way a live level's do.
            this.world.Load(min.X, max.X, min.Y, max.Y, min.Z, max.Z, ringChunks: 1);
        }

        public ValueTask SetBlockAsync(Vector position, IBlock block, IReadOnlyList<ScenarioItem> items)
        {
            Assert.Empty(items);
            RedstoneObserver.PrepareCommandPlacement(this.world, position, block);
            return this.world.Place(position, block);
        }

        // One game tick: block ticks, then the simulation work and notifications they queued (the level's order).
        public ValueTask TickAsync(IReadOnlyList<Vector> contacts, IReadOnlyList<ArrowHit> arrowHits) =>
            this.world.Advance(contacts, arrowHits);

        public ValueTask ReloadAsync()
        {
            this.world.Restart();
            return ValueTask.CompletedTask;
        }

        public ValueTask<IBlock> GetBlockAsync(Vector position)
        {
            var block = this.world.GetLoadedBlock(position);
            return ValueTask.FromResult(block ?? throw new InvalidOperationException($"{position} isn't loaded."));
        }

        public ValueTask<int> CountItemsAsync(Vector min, Vector max, string item) =>
            throw new NotSupportedException("Dropped items need a level scenario.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// The replay's world for scenarios that need block entities: a level of a test server, with empty chunks around
    /// the scenario's box and a player above it, so that its blocks and block entities tick. Blocks are set with block
    /// updates, like <c>/setblock</c>, and the server's world ticks as a whole.
    /// </summary>
    private sealed class LevelScenarioWorld : IRedstoneScenarioWorld
    {
        // Chunks loaded on each side of the box's chunks, so that the box's chunks tick as a player's do.
        private const int ChunkRing = 2;

        private readonly RealServerContext context = TestServerFactory.CreateServer($"redstone-{Guid.NewGuid():N}");
        private readonly HashSet<(int X, int Z)> loaded = [];

        public async ValueTask SetBlockAsync(Vector position, IBlock block, IReadOnlyList<ScenarioItem> items)
        {
            this.context.World.PrepareObserverCommandPlacement(position, block);
            await this.context.World.SetBlockAsync(position, block, doBlockUpdate: true);
            if (items.Count == 0)
                return;

            // Like /setblock with Items data: the block entity holds them from its placement.
            var entity = await this.context.World.GetBlockEntityAsync(position);
            var container = Assert.IsAssignableFrom<ContainerBlockEntity>(entity);
            foreach (var item in items)
                container.Inventory.SetItem(item.Slot, new ItemStack(ItemsRegistry.Get(item.Id), item.Count));
        }

        public async ValueTask TickAsync(IReadOnlyList<Vector> contacts, IReadOnlyList<ArrowHit> arrowHits)
        {
            Assert.Empty(contacts);
            Assert.Empty(arrowHits);

            await this.context.World.DoWorldTickAsync();
        }

        public ValueTask ReloadAsync() => throw new NotSupportedException("Level scenarios don't reload.");

        public async ValueTask<IBlock> GetBlockAsync(Vector position)
        {
            var block = await this.context.World.GetBlockAsync(position);
            return block ?? throw new InvalidOperationException($"{position} isn't loaded.");
        }

        // Like the capture's dx/dy/dz selector: items whose boxes reach into the blocks from min to max.
        public ValueTask<int> CountItemsAsync(Vector min, Vector max, string item)
        {
            var from = new VectorD(min.X, min.Y, min.Z);
            var to = new VectorD(max.X + 1, max.Y + 1, max.Z + 1);
            var center = (from + to) / 2;
            var radius = (float)VectorD.Distance(from, to);

            var count = this.context.World.GetEntitiesInRange(center, radius)
                .OfType<ItemEntity>()
                .Where(drop => drop.Item.Holder.UnlocalizedName == item && Overlaps(drop.BoundingBox, from, to))
                .Sum(drop => drop.Item.Count);
            return ValueTask.FromResult(count);
        }

        public ValueTask DisposeAsync() => this.context.DisposeAsync();

        private static bool Overlaps(BoundingBox box, VectorD from, VectorD to) =>
            box.Min.X <= to.X && box.Max.X >= from.X
            && box.Min.Y <= to.Y && box.Max.Y >= from.Y
            && box.Min.Z <= to.Z && box.Max.Z >= from.Z;

        // The fixture's level boxes are disjoint and farther apart than redstone reaches. Keep their terrain
        // and server across cases; a player above each box keeps it ticking without replacing shared chunks.
        public async ValueTask PrepareAsync(JsonElement scenario)
        {
            var min = ReadVector(scenario.GetProperty("min"));
            var max = ReadVector(scenario.GetProperty("max"));
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

            var above = new VectorD((min.X + max.X) / 2.0, max.Y + 2, (min.Z + max.Z) / 2.0);
            var player = TestServerFactory.CreateConnectedPlayer(
                this.context.Server, world, $"Watcher{this.context.World.Players.Count}", above);
            await TestServerFactory.JoinConnectedPlayerAsync(this.context.Server, player);
        }
    }

    /// <summary>An item a setup placement puts in its block entity's <see cref="Slot"/>.</summary>
    public sealed record ScenarioItem(int Slot, string Id, int Count);

    private static void AssertStateRoundTrips(string state, string context)
    {
        var (name, properties) = ParseStateText(state);
        var block = BlockStateProperties.GetState(name, properties);
        Assert.Equal(name, block.UnlocalizedName);

        // GetState falls back to the default state for an unknown property value, so the values are compared as well.
        foreach (var (key, value) in properties)
            Assert.True(block.GetProperty(key)?.ToString() == value, $"{context}: {state} does not keep {key}={value}");
    }

    private static (string Name, Dictionary<string, string> Properties) ParseStateText(string state)
    {
        var bracket = state.IndexOf('[');
        if (bracket < 0)
            return (state, []);

        var properties = state[(bracket + 1)..^1].Split(',').Select(property => property.Split('='))
            .ToDictionary(pair => pair[0], pair => pair[1]);
        return (state[..bracket], properties);
    }

    private static Vector ReadVector(JsonElement array) => new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    private static VectorD ReadLocation(JsonElement array) => new(array[0].GetDouble(), array[1].GetDouble(), array[2].GetDouble());

    private static bool IsInside(Vector position, Vector min, Vector max) =>
        position.X >= min.X && position.X <= max.X && position.Y >= min.Y && position.Y <= max.Y && position.Z >= min.Z && position.Z <= max.Z;

    /// <summary>
    /// The world a replay drives. Mirrors the oracle: <see cref="SetBlockAsync"/> is a live block change with neighbor
    /// updates (like <c>/setblock</c>, with its block entity's items), <see cref="TickAsync"/> advances one game tick
    /// and runs the work due in it, and <see cref="ReloadAsync"/> saves and loads the world, which must keep scheduled
    /// work (block ticks) with its delays.
    /// </summary>
    public interface IRedstoneScenarioWorld : IAsyncDisposable
    {
        ValueTask SetBlockAsync(Vector position, IBlock block, IReadOnlyList<ScenarioItem> items);

        /// <summary>One game tick, with the entities' contact cells for that tick.</summary>
        ValueTask TickAsync(IReadOnlyList<Vector> contacts, IReadOnlyList<ArrowHit> arrowHits);

        ValueTask ReloadAsync();

        ValueTask<IBlock> GetBlockAsync(Vector position);

        /// <summary>
        /// The total count of <paramref name="item"/> lying as item entities in the blocks from min to max.
        /// </summary>
        ValueTask<int> CountItemsAsync(Vector min, Vector max, string item);
    }

    /// <summary>
    /// Replays a fixture scenario in the order it was recorded: setup at tick 0, then for each tick t from 1 to the last
    /// tick, the tick's reloads (a reload is followed by its recorded number of ticks) and actions, then one tick. Each
    /// checkpoint is compared with the world's states, and every difference is reported.
    /// </summary>
    public static class RedstoneReplay
    {
        public static async Task<List<string>> CompareAsync(IRedstoneScenarioWorld world, JsonElement scenario)
        {
            var (min, max) = (ReadVector(scenario.GetProperty("min")), ReadVector(scenario.GetProperty("max")));
            foreach (var placement in scenario.GetProperty("setup").EnumerateArray())
            {
                var position = ReadVector(placement.GetProperty("pos"));
                var state = ParseState(placement.GetProperty("state").GetString()!);
                await world.SetBlockAsync(position, state, ReadItems(placement));
            }

            var actions = scenario.GetProperty("actions").EnumerateArray().ToArray();
            var entities = scenario.TryGetProperty("entities", out var entityEvents) ? entityEvents.EnumerateArray().ToArray() : [];
            var arrows = scenario.TryGetProperty("arrows", out var arrowShots) ? arrowShots.EnumerateArray().ToArray() : [];
            IReadOnlyList<Vector> contacts = [];
            var checkpoints = scenario.GetProperty("checkpoints").EnumerateArray().ToDictionary(checkpoint => checkpoint.GetProperty("tick").GetInt32());
            var item = scenario.TryGetProperty("item", out var itemId) ? itemId.GetString() : null;
            var diffs = new List<string>();
            diffs.AddRange(await CompareAsync(world, checkpoints[0], min, max, item));

            for (var tick = 1; tick < checkpoints.Count; tick++)
            {
                var tickActions = actions.Where(action => action.GetProperty("tick").GetInt32() == tick).ToArray();
                foreach (var action in tickActions.Where(action => action.TryGetProperty("reload", out _)))
                {
                    await world.ReloadAsync();
                    for (var reloadTick = 0; reloadTick < action.GetProperty("ticks").GetInt32(); reloadTick++)
                        await world.TickAsync(contacts, []);
                }

                foreach (var action in tickActions.Where(action => !action.TryGetProperty("reload", out _)))
                {
                    var position = ReadVector(action.GetProperty("pos"));
                    var state = ParseState(action.GetProperty("state").GetString()!);
                    await world.SetBlockAsync(position, state, []);
                }

                // The entity contact set changes at its events and holds until the next one, as entities do in the level.
                foreach (var contact in entities.Where(contact => contact.GetProperty("tick").GetInt32() == tick))
                    contacts = contact.GetProperty("contacts").EnumerateArray().Select(ReadVector).ToArray();

                // Arrows that enter a block this tick hit after the block ticks, like the entity pass.
                var hits = arrows.Where(arrow => arrow.GetProperty("tick").GetInt32() == tick)
                    .Select(arrow => new ArrowHit(ReadVector(arrow.GetProperty("block")), ReadLocation(arrow.GetProperty("location")),
                        Enum.Parse<BlockFace>(arrow.GetProperty("face").GetString()!, ignoreCase: true))).ToArray();

                await world.TickAsync(contacts, hits);
                diffs.AddRange(await CompareAsync(world, checkpoints[tick], min, max, item));
            }

            return diffs;
        }

        private static ScenarioItem[] ReadItems(JsonElement placement)
        {
            if (!placement.TryGetProperty("items", out var items))
                return [];

            return items.EnumerateArray()
                .Select(item => new ScenarioItem(
                    item.GetProperty("slot").GetInt32(),
                    item.GetProperty("id").GetString()!,
                    item.GetProperty("count").GetInt32()))
                .ToArray();
        }

        private static async Task<List<string>> CompareAsync(
            IRedstoneScenarioWorld world,
            JsonElement checkpoint,
            Vector min,
            Vector max,
            string? item)
        {
            var differences = new List<string>();
            var tick = checkpoint.GetProperty("tick").GetInt32();

            // The capture counts the item within two blocks of the box.
            if (item is not null)
            {
                var margin = new Vector(2, 2, 2);
                var expectedItems = checkpoint.GetProperty("items").GetInt32();
                var actualItems = await world.CountItemsAsync(min - margin, max + margin, item);
                if (actualItems != expectedItems)
                    differences.Add($"tick {tick}: vanilla has {expectedItems} {item} dropped, ours {actualItems}");
            }

            var paletteText = checkpoint.GetProperty("palette").EnumerateArray().Select(state => state.GetString()!).ToArray();
            var palette = paletteText.Select(ParseState).ToArray();
            var rows = checkpoint.GetProperty("rows").EnumerateArray().Select(row => row.GetString()!).ToArray();
            var depth = max.Z - min.Z + 1;
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var z = min.Z; z <= max.Z; z++)
                {
                    var row = rows[(y - min.Y) * depth + (z - min.Z)];
                    for (var x = min.X; x <= max.X; x++)
                    {
                        var index = Symbols.IndexOf(row[x - min.X]);
                        var expected = palette[index];
                        var actual = await world.GetBlockAsync(new Vector(x, y, z));
                        if (actual.IsSameState(expected))
                            continue;

                        var ours = Describe(actual, paletteText[index]);
                        differences.Add($"tick {tick} ({x},{y},{z}): vanilla {paletteText[index]}, ours {ours}");
                    }
                }
            }

            return differences;
        }

        // The block's state with the same properties, in the order the vanilla state text lists them.
        private static string Describe(IBlock block, string vanillaState)
        {
            var bracket = vanillaState.IndexOf('[');
            if (bracket < 0)
                return block.UnlocalizedName;

            var keys = vanillaState[(bracket + 1)..^1].Split(',').Select(property => property.Split('=')[0]);
            return $"{block.UnlocalizedName}[{string.Join(",", keys.Select(key => $"{key}={block.GetProperty(key)}"))}]";
        }

        private static IBlock ParseState(string state)
        {
            var (name, properties) = ParseStateText(state);
            return BlockStateProperties.GetState(name, properties);
        }

        private static (string Name, Dictionary<string, string> Properties) ParseStateText(string state) => VanillaRedstone.ParseStateText(state);

        private static Vector ReadVector(JsonElement array) => VanillaRedstone.ReadVector(array);
    }

    /// <summary>
    /// Loads the redstone scenarios once for every test.
    /// </summary>
    public sealed class Fixture
    {
        private static readonly Lazy<Fixture> shared = new(() => new Fixture());

        private Fixture()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Tests.Assets.redstone_scenarios.json")!;
            this.Document = JsonDocument.Parse(stream).RootElement.Clone();
            this.Scenarios = this.Document.GetProperty("scenarios").EnumerateArray()
                .ToDictionary(scenario => scenario.GetProperty("name").GetString()!, scenario => scenario.Clone());
        }

        public static Fixture Shared => shared.Value;

        public JsonElement Document { get; }

        public IReadOnlyDictionary<string, JsonElement> Scenarios { get; }
    }
}
