using Obsidian.API;
using Obsidian.Registries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Redstone parity fixtures (R0, see docs/redstone/01-foundation.md). Each scenario in <c>Assets/redstone_scenarios.json</c>
/// was recorded from a headless vanilla 1.21.11 server by <c>tools/redstone/generate.py</c>: the server's game time is
/// frozen and advanced one tick at a time, and every checkpoint holds the block states of the scenario's box.
/// </summary>
/// <remarks>
/// The fixture tests run now: they check that the recorded data is complete, consistent and written in states Obsidian
/// knows. The replay tests need the directional power engine (R3) and stay skipped until a world adapter exists; they
/// use <see cref="RedstoneReplay"/>, which replays a scenario with the timing the fixture documents.
/// </remarks>
public class VanillaRedstone
{
    private const string Symbols = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>The categories the R0 fixture must cover (docs/redstone/01-foundation.md, "Initial fixture coverage").</summary>
    private static readonly string[] RequiredCoverage =
    [
        "source_timing", "dust_attenuation", "repeater_delay", "repeater_lock", "comparator_modes", "observer_pulses",
        "torch_inversion", "torch_burnout", "save_reload",
    ];

    public static IEnumerable<object[]> ScenarioNames() => Fixture.Shared.Scenarios.Keys.Select(name => new object[] { name });

    [Fact]
    public void FixtureIsForVanilla1_21_11() =>
        Assert.Equal("1.21.11", Fixture.Shared.Document.GetProperty("version").GetString());

    [Fact]
    public void FixtureCoversTheRequiredCategories()
    {
        var covered = Fixture.Shared.Scenarios.Values
            .SelectMany(scenario => scenario.GetProperty("coverage").EnumerateArray().Select(tag => tag.GetString()!))
            .ToHashSet();

        Assert.Empty(RequiredCoverage.Where(tag => !covered.Contains(tag)));
    }

    [Fact]
    public void ComparatorFixtureCoversCompareAndSubtractAtSidePower14()
    {
        var scenario = Fixture.Shared.Scenarios["comparator_compare_and_subtract"];
        var palette = scenario.GetProperty("checkpoints").EnumerateArray()
            .SelectMany(checkpoint => checkpoint.GetProperty("palette").EnumerateArray())
            .Select(state => state.GetString()!).ToHashSet();

        Assert.Contains("minecraft:comparator[facing=west,mode=compare,powered=true]", palette);
        Assert.Contains("minecraft:comparator[facing=west,mode=compare,powered=false]", palette);
        Assert.Contains("minecraft:comparator[facing=west,mode=subtract,powered=true]", palette);
        Assert.Contains("minecraft:comparator[facing=west,mode=subtract,powered=false]", palette);
        Assert.Contains(palette, state => state.StartsWith("minecraft:redstone_wire[") && state.Contains("power=14"));
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void ScenarioIsWellFormed(string name)
    {
        var scenario = Fixture.Shared.Scenarios[name];
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

    // Each replay runs the scenario on the live engine and compares every checkpoint with vanilla.
    /// <summary>The scenarios R4 covers: levers, dust, and redstone torches (inversion and burnout).</summary>
    public static IEnumerable<object[]> R4ScenarioNames() =>
        new[] { "source_timing_lever", "dust_attenuation", "torch_inversion", "torch_burnout" }.Select(name => new object[] { name });

    /// <summary>The scenarios implemented in the repeater pass of R6.</summary>
    public static IEnumerable<object[]> RepeaterScenarioNames() =>
        new[] { "repeater_delay", "repeater_lock" }.Select(name => new object[] { name });

    /// <summary>The scenarios implemented in the observer pass of R6.</summary>
    public static IEnumerable<object[]> ObserverScenarioNames() =>
        new[] { "observer_pulse" }.Select(name => new object[] { name });

    /// <summary>The scenarios implemented in the comparator pass of R6.</summary>
    public static IEnumerable<object[]> ComparatorScenarioNames() =>
        new[] { "comparator_compare_and_subtract" }.Select(name => new object[] { name });

    /// <summary>The scenarios implemented in the piston pass of R8: extension, retraction, sticky pulls, and immovable blocks.</summary>
    public static IEnumerable<object[]> PistonScenarioNames() =>
        new[] { "piston_extend_retract", "piston_sticky_pull", "piston_obsidian_blocked" }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(PistonScenarioNames))]
    public void PistonScenarioMatchesVanilla(string name)
    {
        var scenario = Fixture.Shared.Scenarios[name];
        var diffs = RedstoneReplay.Compare(CreateWorld(scenario), scenario);
        Assert.True(diffs.Count == 0, $"{diffs.Count} blocks differ from vanilla:\n{string.Join("\n", diffs.Take(20))}");
    }

    [Theory]
    [MemberData(nameof(R4ScenarioNames))]
    public void ScenarioMatchesVanilla(string name)
    {
        var scenario = Fixture.Shared.Scenarios[name];
        var diffs = RedstoneReplay.Compare(CreateWorld(scenario), scenario);
        Assert.True(diffs.Count == 0, $"{diffs.Count} blocks differ from vanilla:\n{string.Join("\n", diffs.Take(20))}");
    }

    [Theory]
    [MemberData(nameof(RepeaterScenarioNames))]
    public void RepeaterScenarioMatchesVanilla(string name)
    {
        var scenario = Fixture.Shared.Scenarios[name];
        var diffs = RedstoneReplay.Compare(CreateWorld(scenario), scenario);
        Assert.True(diffs.Count == 0, $"{diffs.Count} blocks differ from vanilla:\n{string.Join("\n", diffs.Take(20))}");
    }

    [Theory]
    [MemberData(nameof(ObserverScenarioNames))]
    public void ObserverScenarioMatchesVanilla(string name)
    {
        var scenario = Fixture.Shared.Scenarios[name];
        var diffs = RedstoneReplay.Compare(CreateWorld(scenario), scenario);
        Assert.True(diffs.Count == 0, $"{diffs.Count} blocks differ from vanilla:\n{string.Join("\n", diffs.Take(20))}");
    }

    /// <summary>The scenarios implemented in the tripwire pass of R4 (tripwire and hooks).</summary>
    public static IEnumerable<object[]> TripwireScenarioNames() =>
        new[] { "tripwire_line" }.Select(name => new object[] { name });

    /// <summary>The scenarios implemented in the target pass of R4 (target block and arrow hits).</summary>
    public static IEnumerable<object[]> TargetScenarioNames() =>
        new[] { "target_arrow" }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(TargetScenarioNames))]
    public void TargetScenarioMatchesVanilla(string name)
    {
        var scenario = Fixture.Shared.Scenarios[name];
        var diffs = RedstoneReplay.Compare(CreateWorld(scenario), scenario);
        Assert.True(diffs.Count == 0, $"{diffs.Count} blocks differ from vanilla:\n{string.Join("\n", diffs.Take(20))}");
    }

    [Theory]
    [MemberData(nameof(TripwireScenarioNames))]
    public void TripwireScenarioMatchesVanilla(string name)
    {
        var scenario = Fixture.Shared.Scenarios[name];
        var diffs = RedstoneReplay.Compare(CreateWorld(scenario), scenario);
        Assert.True(diffs.Count == 0, $"{diffs.Count} blocks differ from vanilla:\n{string.Join("\n", diffs.Take(20))}");
    }

    [Theory]
    [MemberData(nameof(ComparatorScenarioNames))]
    public void ComparatorScenarioMatchesVanilla(string name)
    {
        var scenario = Fixture.Shared.Scenarios[name];
        var diffs = RedstoneReplay.Compare(CreateWorld(scenario), scenario);
        Assert.True(diffs.Count == 0, $"{diffs.Count} blocks differ from vanilla:\n{string.Join("\n", diffs.Take(20))}");
    }

    /// <summary>The scenarios that save and restart the world mid-run (block ticks must keep their delays).</summary>
    public static IEnumerable<object[]> ReloadScenarioNames() =>
        new[] { "save_reload_repeater", "reload_target_pulse" }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ReloadScenarioNames))]
    public void ReloadScenarioMatchesVanilla(string name)
    {
        var scenario = Fixture.Shared.Scenarios[name];
        var diffs = RedstoneReplay.Compare(CreateWorld(scenario), scenario);
        Assert.True(diffs.Count == 0, $"{diffs.Count} blocks differ from vanilla:\n{string.Join("\n", diffs.Take(20))}");
    }

    /// <summary>
    /// Runs every scenario's timeline through a world with no redstone at all: this checks the replay itself (setup,
    /// actions, reloads and ticks in the documented order), not the redstone behavior.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void ReplayRunsTheWholeTimeline(string name)
    {
        var scenario = Fixture.Shared.Scenarios[name];
        var world = new StaticWorld();

        var diffs = RedstoneReplay.Compare(world, scenario);

        var reloads = scenario.GetProperty("actions").EnumerateArray().Where(action => action.TryGetProperty("reload", out _)).ToArray();
        var reloadTicks = reloads.Sum(action => action.GetProperty("ticks").GetInt32());
        Assert.Equal(scenario.GetProperty("checkpoints").GetArrayLength() - 1 + reloadTicks, world.Ticks);
        Assert.Equal(reloads.Length, world.Reloads);

        // Obsidian holds the piston in place, so vanilla's box matches a world with no redstone at all.
        if (UnchangedWithoutRedstone.Contains(name))
            Assert.Empty(diffs);
        else
            Assert.NotEmpty(diffs);
    }

    private static readonly HashSet<string> UnchangedWithoutRedstone = ["piston_obsidian_blocked"];

    private static IRedstoneScenarioWorld CreateWorld(JsonElement scenario) => new EngineScenarioWorld(scenario);

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

        public void SetBlock(Vector position, IBlock block) => this.world.Place(position, block).AsTask().GetAwaiter().GetResult();


        // One game tick: block ticks, then the simulation work and notifications they queued (the level's order).
        public void Tick(IReadOnlyList<Vector> contacts, IReadOnlyList<ArrowHit> arrowHits) =>
            this.world.Advance(contacts, arrowHits).AsTask().GetAwaiter().GetResult();

        public void Reload() => this.world.Restart();

        public IBlock GetBlock(Vector position) => this.world.GetLoadedBlock(position) ?? throw new InvalidOperationException($"{position} isn't loaded.");
    }

    private sealed class StaticWorld : IRedstoneScenarioWorld
    {
        private readonly Dictionary<(int X, int Y, int Z), IBlock> blocks = [];

        public int Ticks { get; private set; }

        public int Reloads { get; private set; }

        public void SetBlock(Vector position, IBlock block) => this.blocks[(position.X, position.Y, position.Z)] = block;


        public void Tick(IReadOnlyList<Vector> contacts, IReadOnlyList<ArrowHit> arrowHits) => this.Ticks++;

        public void Reload() => this.Reloads++;

        public IBlock GetBlock(Vector position) =>
            this.blocks.GetValueOrDefault((position.X, position.Y, position.Z), BlockStateProperties.GetState("minecraft:air"));
    }

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
    /// The world a replay drives. Mirrors the oracle: <see cref="SetBlock"/> is a live block change with neighbor updates
    /// (like <c>/setblock</c>), <see cref="Tick"/> advances one game tick and runs the work due in it, and
    /// <see cref="Reload"/> saves and loads the world, which must keep scheduled work (block ticks) with its delays.
    /// </summary>
    public interface IRedstoneScenarioWorld
    {
        void SetBlock(Vector position, IBlock block);


        /// <summary>One game tick, with the entities' contact cells for that tick.</summary>
        void Tick(IReadOnlyList<Vector> contacts, IReadOnlyList<ArrowHit> arrowHits);

        void Reload();

        IBlock GetBlock(Vector position);
    }

    /// <summary>
    /// Replays a fixture scenario in the order it was recorded: setup at tick 0, then for each tick t from 1 to the last
    /// tick, the tick's reloads (a reload is followed by its recorded number of ticks) and actions, then one tick. Each
    /// checkpoint is compared with the world's states, and every difference is reported.
    /// </summary>
    public static class RedstoneReplay
    {
        public static List<string> Compare(IRedstoneScenarioWorld world, JsonElement scenario)
        {
            var (min, max) = (ReadVector(scenario.GetProperty("min")), ReadVector(scenario.GetProperty("max")));
            foreach (var placement in scenario.GetProperty("setup").EnumerateArray())
                world.SetBlock(ReadVector(placement.GetProperty("pos")), ParseState(placement.GetProperty("state").GetString()!));

            var actions = scenario.GetProperty("actions").EnumerateArray().ToArray();
            var entities = scenario.TryGetProperty("entities", out var entityEvents) ? entityEvents.EnumerateArray().ToArray() : [];
            var arrows = scenario.TryGetProperty("arrows", out var arrowShots) ? arrowShots.EnumerateArray().ToArray() : [];
            IReadOnlyList<Vector> contacts = [];
            var checkpoints = scenario.GetProperty("checkpoints").EnumerateArray().ToDictionary(checkpoint => checkpoint.GetProperty("tick").GetInt32());
            var diffs = new List<string>();
            diffs.AddRange(Compare(world, checkpoints[0], min, max));

            for (var tick = 1; tick < checkpoints.Count; tick++)
            {
                var tickActions = actions.Where(action => action.GetProperty("tick").GetInt32() == tick).ToArray();
                foreach (var action in tickActions.Where(action => action.TryGetProperty("reload", out _)))
                {
                    world.Reload();
                    for (var reloadTick = 0; reloadTick < action.GetProperty("ticks").GetInt32(); reloadTick++)
                        world.Tick(contacts, []);
                }

                foreach (var action in tickActions.Where(action => !action.TryGetProperty("reload", out _)))
                    world.SetBlock(ReadVector(action.GetProperty("pos")), ParseState(action.GetProperty("state").GetString()!));

                // The entity contact set changes at its events and holds until the next one, as entities do in the level.
                foreach (var contact in entities.Where(contact => contact.GetProperty("tick").GetInt32() == tick))
                    contacts = contact.GetProperty("contacts").EnumerateArray().Select(ReadVector).ToArray();

                // Arrows that enter a block this tick hit after the block ticks, like the entity pass.
                var hits = arrows.Where(arrow => arrow.GetProperty("tick").GetInt32() == tick)
                    .Select(arrow => new ArrowHit(ReadVector(arrow.GetProperty("block")), ReadLocation(arrow.GetProperty("location")),
                        Enum.Parse<BlockFace>(arrow.GetProperty("face").GetString()!, ignoreCase: true))).ToArray();

                world.Tick(contacts, hits);
                diffs.AddRange(Compare(world, checkpoints[tick], min, max));
            }

            return diffs;
        }

        private static IEnumerable<string> Compare(IRedstoneScenarioWorld world, JsonElement checkpoint, Vector min, Vector max)
        {
            var tick = checkpoint.GetProperty("tick").GetInt32();
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
                        var actual = world.GetBlock(new Vector(x, y, z));
                        if (!actual.IsSameState(expected))
                            yield return $"tick {tick} ({x},{y},{z}): vanilla {paletteText[index]}, ours {Describe(actual, paletteText[index])}";
                    }
                }
            }
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
