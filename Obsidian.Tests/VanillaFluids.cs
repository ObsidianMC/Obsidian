using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Fluids;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Fluid parity: small worlds run through Obsidian's fluid ticks are compared with vanilla 1.21.11 running the same
/// scenarios with its own fluid code (<c>Assets/fluid_scenarios.json</c>, written by mcdecomp's harness
/// <c>FluidDump.java</c>). The checkpoints catch differences in spreading, slope finding, decay, source conversion,
/// lava and water interactions, waterlogged blocks and tick timing.
/// </summary>
public class VanillaFluids(VanillaFluids.ScenarioFixture fixture) : IClassFixture<VanillaFluids.ScenarioFixture>
{
    private const string Symbols = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [Theory]
    [InlineData("water_flat")]
    [InlineData("water_slope_to_hole")]
    [InlineData("waterfall_and_decay")]
    [InlineData("water_source_conversion")]
    [InlineData("lava_and_water")]
    [InlineData("nether_fast_lava_and_basalt")]
    [InlineData("waterlogged_and_obstacles")]
    public void ScenarioMatchesVanilla(string name)
    {
        var scenario = fixture.Scenarios[name];
        var world = new ScenarioWorld(new FluidRules(scenario.GetProperty("fastLava").GetBoolean(),
            scenario.GetProperty("waterSourceConversion").GetBoolean(), scenario.GetProperty("lavaSourceConversion").GetBoolean()));

        // Setup fills place blocks without any updates.
        foreach (var fill in scenario.GetProperty("setup").EnumerateArray())
        {
            var block = ParseState(fill.GetProperty("state").GetString()!);
            var (from, to) = (ReadVector(fill.GetProperty("from")), ReadVector(fill.GetProperty("to")));
            for (var x = from.X; x <= to.X; x++)
            {
                for (var y = from.Y; y <= to.Y; y++)
                {
                    for (var z = from.Z; z <= to.Z; z++)
                        world.SetBlock(new Vector(x, y, z), block);
                }
            }
        }

        var actions = scenario.GetProperty("actions").EnumerateArray()
            .Select(action => (Tick: action.GetProperty("tick").GetInt32(), Position: ReadVector(action.GetProperty("pos")),
                Block: ParseState(action.GetProperty("state").GetString()!)))
            .ToList();
        var checkpoints = scenario.GetProperty("checkpoints").EnumerateArray().ToDictionary(checkpoint => checkpoint.GetProperty("tick").GetInt32());
        var (min, max) = (ReadVector(scenario.GetProperty("min")), ReadVector(scenario.GetProperty("max")));

        // Like a server tick: actions (block changes with updates) first, then the game time advances and fluid ticks run.
        var diffs = new List<string>();
        for (var tick = 0; tick <= checkpoints.Keys.Max(); tick++)
        {
            foreach (var action in actions.Where(action => action.Tick == tick))
                world.Fluids.SetBlock(action.Position, action.Block, BlockUpdateFlags.All);

            if (tick > 0)
                world.Scheduler.Tick(_ => true, world.Fluids.RunScheduledTick);

            if (checkpoints.TryGetValue(tick, out var checkpoint))
                diffs.AddRange(Compare(world, checkpoint, min, max));
        }

        Assert.True(diffs.Count == 0, $"{diffs.Count} blocks differ from vanilla:\n{string.Join("\n", diffs.Take(20))}");
    }

    private static IEnumerable<string> Compare(ScenarioWorld world, JsonElement checkpoint, Vector min, Vector max)
    {
        var tick = checkpoint.GetProperty("tick").GetInt32();
        var palette = checkpoint.GetProperty("palette").EnumerateArray().Select(state => ParseState(state.GetString()!)).ToArray();
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
                    var actual = world.GetBlock(new Vector(x, y, z));
                    if (!actual.IsSameState(expected))
                        yield return $"tick {tick} ({x},{y},{z}): vanilla {Describe(expected)}, ours {Describe(actual)}";
                }
            }
        }
    }

    private static Vector ReadVector(JsonElement array) =>
        new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    // Vanilla's block state syntax, e.g. "minecraft:water[level=3]".
    private static IBlock ParseState(string state)
    {
        var bracket = state.IndexOf('[');
        if (bracket < 0)
            return BlockStateProperties.GetState(state);

        var properties = state[(bracket + 1)..^1].Split(',').Select(property => property.Split('='))
            .ToDictionary(pair => pair[0], pair => pair[1]);
        return BlockStateProperties.GetState(state[..bracket], properties);
    }

    private static string Describe(IBlock block)
    {
        var level = block.GetProperty("level");
        var waterlogged = block.GetProperty("waterlogged");
        return block.UnlocalizedName + (level is not null ? $"[level={level}]" : waterlogged is not null ? $"[waterlogged={waterlogged}]" : "");
    }

    /// <summary>
    /// Loads the vanilla scenarios once for every test.
    /// </summary>
    public sealed class ScenarioFixture
    {
        public ScenarioFixture()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Tests.Assets.fluid_scenarios.json")!;
            using var document = JsonDocument.Parse(stream);
            this.Scenarios = document.RootElement.GetProperty("scenarios").EnumerateArray()
                .ToDictionary(scenario => scenario.GetProperty("name").GetString()!, scenario => scenario.Clone());
        }

        public IReadOnlyDictionary<string, JsonElement> Scenarios { get; }
    }

    /// <summary>
    /// An empty world like the harness's: chunks from Y 0 to 31 created on first use, every chunk ticking.
    /// </summary>
    private sealed class ScenarioWorld : IFluidLevelAccess
    {
        private readonly Dictionary<(int X, int Z), Chunk> chunks = [];

        public ScenarioWorld(FluidRules rules)
        {
            this.Rules = rules;
            this.Fluids = new FluidLevel(this);
        }

        public FluidTickScheduler Scheduler { get; } = new();

        public FluidLevel Fluids { get; }

        public int MinY => 0;

        public int Height => 32;

        public FluidRules Rules { get; }

        public Random Random { get; } = new(0);

        public IBlock GetBlock(Vector position) =>
            this.IsOutsideBuildHeight(position.Y) ? BlocksRegistry.VoidAir : this.Chunk(position).GetBlock(position.X, position.Y, position.Z);

        public bool SetBlock(Vector position, IBlock block)
        {
            if (this.IsOutsideBuildHeight(position.Y))
                return false;

            this.Chunk(position).SetBlock(position.X, position.Y, position.Z, block);
            return true;
        }

        public void ScheduleFluidTick(Vector position, FluidKind fluid, int delay) => this.Chunk(position).FluidTicks.Schedule(position, fluid, delay);

        private bool IsOutsideBuildHeight(int y) => y < this.MinY || y >= this.MinY + this.Height;

        private Chunk Chunk(Vector position)
        {
            var key = (position.X >> 4, position.Z >> 4);
            if (!this.chunks.TryGetValue(key, out var chunk))
            {
                this.chunks[key] = chunk = new Chunk(key.Item1, key.Item2, this.MinY, this.Height, ChunkGenStage.full);
                chunk.FluidTicks.StartTicking(this.Scheduler, FluidTickScheduler.ChunkKey(position));
            }

            return chunk;
        }
    }
}
