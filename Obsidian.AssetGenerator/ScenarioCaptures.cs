using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.AssetGenerator;

/// <summary>
/// Re-records the tick-by-tick vanilla scenarios the redstone, fluid and daylight detector parity tests replay
/// (<c>Obsidian.Tests/Assets/redstone_scenarios.json</c>, <c>fluid_scenarios.json</c> and
/// <c>daylight_detector.json</c>), in place, on a headless vanilla server (<see cref="ScenarioServer"/>).
/// </summary>
/// <remarks>
/// Each fixture is its own input: the capture keeps every scenario's definition (its box, setup, actions, entities
/// and arrows, or the detector's sample times) and replaces only what vanilla produced, so a scenario is added or
/// changed by editing its definition and capturing again. Only runs on request (<c>dotnet build
/// Obsidian.AssetGenerator -t:CaptureVanillaScenarios</c>).
/// </remarks>
internal static class ScenarioCaptures
{
    public const string Redstone = "redstone";
    public const string Fluids = "fluids";
    public const string Daylight = "daylight";
    public const string Sulfur = "sulfur";
    public const string MoreBlocks = "more_blocks";
    public const string StructurePieces = "structure_pieces";

    private const string Symbols = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // Scenario entities and arrows carry this tag, so they can be removed again.
    private const string EntityTag = "scenario";

    // Ticks run after a scenario's blocks are cleared, so that work it left scheduled finds air and is dropped before
    // the next scenario places blocks there.
    private const int DrainTicks = 40;

    // Blocks a scenario can turn into without naming them, tried first when reading a changed block.
    // Daylight detectors update when the game time is a multiple of this.
    private const int UpdateInterval = 20;

    private static readonly string[] LikelyIds = ["minecraft:air", "minecraft:moving_piston", "minecraft:piston_head"];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 1,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // The fixtures whose probes record only the cases asked for.
    private static readonly string[] SelectableCases = [Sulfur, MoreBlocks, StructurePieces];

    public static readonly string[] All = [Redstone, Fluids, Daylight, Sulfur, MoreBlocks, StructurePieces];

    private const string RedstoneNotes = """
        Vanilla redstone scenarios, recorded by the asset generator's capture-scenarios (ScenarioCaptures.cs) on a
        headless vanilla server in a void world, whose game is frozen and stepped one tick at a time, with block
        states read with execute if block. Coordinates are absolute. Setup blocks are placed first, live, in order,
        with neighbor updates (/setblock). Each tick t from 1 to the scenario's ticks: its reloads (world saved and
        server restarted, which runs `ticks` game ticks of its own before the server is frozen again), actions,
        entity events (an armor stand standing in each contact cell until the next event) and arrows (flying
        straight into the face at the location, without gravity) happen first, at the previous game time, then the
        game advances one tick. Checkpoints hold the box after each tick: one string per (y, z) row, y then z
        ascending, with a character per x giving the block's palette index in 0-9a-zA-Z. Palette states are full
        states with every property, sorted by name.
        """;

    private const string FluidNotes = """
        Vanilla fluid scenarios, recorded by the asset generator's capture-scenarios (ScenarioCaptures.cs) on a
        headless vanilla server in a void world (the overworld, or a void nether where the scenario's dimension says
        so), whose game is frozen and stepped one tick at a time, with random ticks off. Setup fills place blocks
        without updates (/fill strict). Each tick runs like a server tick: the tick's actions (/setblock, with
        updates) happen first, at the previous game time, then the game time advances to the tick and its scheduled
        ticks run (tick 0 only runs actions). Checkpoints hold the box's blocks after the given tick: one string per
        (y, z) row, y then z ascending, with a character per x giving the block's palette index in 0-9a-zA-Z.
        """;

    private const string DaylightNotes = """
        Vanilla daylight detector readings, recorded by the asset generator's capture-scenarios
        (ScenarioCaptures.cs) on a headless vanilla server: a detector and an inverted one on a stone floor under
        open sky in a void world. For each sample the overworld clock is set to it (the n-th sample at a game time
        of n modulo 20) and the game steps to the next game time that is a multiple of 20, when detectors update.
        dayTime is the clock's time in that tick, and power and inverted the detectors' power.
        """;

    public static async Task RunAsync(VanillaServer server, string assetsDirectory,
        IReadOnlyCollection<string> fixtures, IReadOnlyCollection<string>? cases = null)
    {
        if (cases is not null && (fixtures.Count != 1 || !SelectableCases.Any(fixtures.Contains)))
        {
            throw new ArgumentException(
                "Case selection requires CaptureFixtures=sulfur, more_blocks or structure_pieces.");
        }

        if (fixtures.Contains(StructurePieces))
        {
            var path = Path.Combine(assetsDirectory, "structure_pieces.json");
            await FixtureDumper.DumpStructurePiecesAsync(server, path, cases);
            if (fixtures.Count == 1)
                return;
        }

        if (fixtures.Contains(MoreBlocks))
        {
            await FixtureDumper.DumpMoreBlocksAsync(server, Path.Combine(assetsDirectory, "more_blocks.json"), cases);
            if (fixtures.Count == 1)
                return;
        }

        if (fixtures.Contains(Sulfur) && cases is null)
        {
            await FixtureDumper.DumpSulfurAsync(server, Path.Combine(assetsDirectory, "sulfur_fixtures.json"));
        }

        await using var vanilla = await ScenarioServer.StartAsync(server,
            fixtures.Contains(Sulfur) ? "test-seed" : null);
        await PrepareAsync(vanilla);

        if (fixtures.Contains(Sulfur))
        {
            var path = Path.Combine(assetsDirectory, "sulfur_scenarios.json");
            await CaptureFileAsync(path, server.Version, ScenarioSulfurCaptures.Notes,
                root => ScenarioSulfurCaptures.CaptureAsync(vanilla, root, cases));
        }

        if (fixtures.Contains(Redstone))
        {
            var path = Path.Combine(assetsDirectory, "redstone_scenarios.json");
            await CaptureFileAsync(path, server.Version, RedstoneNotes, root => CaptureRedstoneAsync(vanilla, root));
        }

        if (fixtures.Contains(Fluids))
        {
            var path = Path.Combine(assetsDirectory, "fluid_scenarios.json");
            await CaptureFileAsync(path, server.Version, FluidNotes, root => CaptureFluidsAsync(vanilla, root));
        }

        if (fixtures.Contains(Daylight))
        {
            var path = Path.Combine(assetsDirectory, "daylight_detector.json");
            await CaptureFileAsync(path, server.Version, DaylightNotes, root => CaptureDaylightAsync(vanilla, root));
        }
    }

    // Nothing but the scenarios changes the world: no random ticks, mobs or weather.
    private static async Task PrepareAsync(ScenarioServer vanilla)
    {
        string[] commands =
        [
            "gamerule minecraft:random_tick_speed 0",
            "gamerule minecraft:spawn_mobs false",
            "gamerule minecraft:advance_weather false",
            "weather clear",
        ];
        foreach (var command in commands)
            await vanilla.RunInAsync(ScenarioServer.Overworld, command);
    }

    private static async Task CaptureFileAsync(string path, string version, string notes,
        Func<JsonObject, Task> capture)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        await capture(root);
        root["_notes"] = notes.ReplaceLineEndings(" ");
        root["version"] = version;

        var text = root.ToJsonString(WriteOptions) + "\n";
        await File.WriteAllTextAsync(path, text.ReplaceLineEndings("\n"), new UTF8Encoding(false));
        Console.WriteLine($"Captured {Path.GetFileName(path)}.");
    }

    // ---------------------------------------------------------------- redstone

    /// <summary>
    /// Each scenario's setup is placed live with <c>/setblock</c>, and checkpoint 0 is read. Then each tick: its
    /// reloads (a save and restart, which runs one tick of its own), its block actions, its entity and arrow events,
    /// one stepped tick, and its checkpoint.
    /// </summary>
    private static async Task CaptureRedstoneAsync(ScenarioServer vanilla, JsonObject root)
    {
        foreach (var scenario in root["scenarios"]!.AsArray().Select(node => node!.AsObject()))
        {
            var name = scenario["name"]!.GetValue<string>();
            Console.WriteLine($"Capturing redstone scenario {name}...");

            var box = Box.Read(scenario);
            var ticks = scenario["checkpoints"]!.AsArray().Count - 1;
            var dimension = ScenarioServer.Overworld;
            await LoadAsync(vanilla, dimension, box.Grow(2));
            await ClearAsync(vanilla, dimension, box.Grow(1));

            foreach (var placement in scenario["setup"]!.AsArray())
                await SetBlockAsync(vanilla, dimension, ReadPosition(placement!["pos"]!), placement["state"]!);

            var reader = new BoxReader(vanilla, dimension, box, StatesIn(scenario));
            var checkpoints = new JsonArray { await reader.ReadAsync(0) };
            var actions = scenario["actions"]!.AsArray();
            for (var tick = 1; tick <= ticks; tick++)
            {
                foreach (var reload in AtTick(actions, tick).Where(action => action["reload"] is not null))
                    reload["ticks"] = await RestartAsync(vanilla);

                foreach (var action in AtTick(actions, tick).Where(action => action["state"] is not null))
                    await SetBlockAsync(vanilla, dimension, ReadPosition(action["pos"]!), action["state"]!);

                foreach (var contacts in AtTick(scenario["entities"]!.AsArray(), tick))
                    await PlaceContactsAsync(vanilla, dimension, contacts["contacts"]!.AsArray());

                foreach (var arrow in AtTick(scenario["arrows"]!.AsArray(), tick))
                    await ShootAsync(vanilla, dimension, arrow);

                await vanilla.StepAsync();
                checkpoints.Add(await reader.ReadAsync(tick));
            }

            scenario["checkpoints"] = checkpoints;
            await RemoveEntitiesAsync(vanilla, dimension);
            await ClearAsync(vanilla, dimension, box.Grow(1));
        }
    }

    // A reload saves the world and restarts the server, which runs one tick before the data pack freezes the game.
    private static async Task<int> RestartAsync(ScenarioServer vanilla)
    {
        var before = await vanilla.GameTimeAsync();
        await vanilla.RestartAsync();
        var after = await vanilla.GameTimeAsync();
        return (int)(after - before);
    }

    // Entities that touch the contact cells from now on: an armor stand standing in each, replacing the earlier ones.
    private static async Task PlaceContactsAsync(ScenarioServer vanilla, string dimension, JsonArray contacts)
    {
        await RemoveEntitiesAsync(vanilla, dimension);
        foreach (var contact in contacts)
        {
            var cell = ReadPosition(contact!);
            var data = $"{{Tags:[\"{EntityTag}\"]}}";
            var x = cell.X + 0.5;
            var z = cell.Z + 0.5;
            var summon = FormattableString.Invariant($"summon minecraft:armor_stand {x} {cell.Y} {z} {data}");
            await vanilla.RunInAsync(dimension, summon);
        }
    }

    // Moved below the world before they're killed, so nothing they drop lands in a scenario.
    private static async Task RemoveEntitiesAsync(ScenarioServer vanilla, string dimension)
    {
        await vanilla.RunInAsync(dimension, $"tp @e[tag={EntityTag}] 0 -1000 0");
        await vanilla.RunInAsync(dimension, $"kill @e[tag={EntityTag}]");
    }

    /// <summary>
    /// An arrow that enters the block's face at the location in the coming tick: it starts half a block in front of the
    /// face and flies straight in, without gravity, so its path crosses the face exactly there.
    /// </summary>
    private static async Task ShootAsync(ScenarioServer vanilla, string dimension, JsonNode arrow)
    {
        var location = arrow["location"]!.AsArray().Select(value => value!.GetValue<double>()).ToArray();
        var (dx, dy, dz) = arrow["face"]!.GetValue<string>() switch
        {
            "west" => (1, 0, 0),
            "east" => (-1, 0, 0),
            "north" => (0, 0, 1),
            "south" => (0, 0, -1),
            "down" => (0, 1, 0),
            "up" => (0, -1, 0),
            var face => throw new InvalidDataException($"Unknown face {face}."),
        };

        var x = location[0] - dx * 0.5;
        var y = location[1] - dy * 0.5;
        var z = location[2] - dz * 0.5;
        var data = $"{{Motion:[{dx}.0d,{dy}.0d,{dz}.0d],NoGravity:1b,Tags:[\"{EntityTag}\"]}}";
        await vanilla.RunInAsync(dimension, FormattableString.Invariant($"summon minecraft:arrow {x} {y} {z} {data}"));
    }

    // ---------------------------------------------------------------- fluids

    /// <summary>
    /// Each scenario's setup fills are placed without updates (<c>/fill … strict</c>). Tick 0 runs only the tick's
    /// actions (<c>/setblock</c>, which updates like <c>Level.setBlock(pos, state, 3)</c>); every later tick runs its
    /// actions, then one stepped tick. The box is read at each checkpoint's tick.
    /// </summary>
    private static async Task CaptureFluidsAsync(ScenarioServer vanilla, JsonObject root)
    {
        foreach (var scenario in root["scenarios"]!.AsArray().Select(node => node!.AsObject()))
        {
            var name = scenario["name"]!.GetValue<string>();
            Console.WriteLine($"Capturing fluid scenario {name}...");

            var box = Box.Read(scenario);
            var dimension = scenario["dimension"]?.GetValue<string>() ?? ScenarioServer.Overworld;
            var fills = scenario["setup"]!.AsArray()
                .Select(fill => new Box(ReadPosition(fill!["from"]!), ReadPosition(fill["to"]!)))
                .ToList();
            var area = fills.Aggregate(box, (union, fill) => union.Union(fill)).Grow(2);
            await LoadAsync(vanilla, dimension, area);
            await ClearAsync(vanilla, dimension, area);

            foreach (var (fill, setup) in fills.Zip(scenario["setup"]!.AsArray()))
            {
                var state = setup!["state"]!.GetValue<string>();
                await vanilla.RunInAsync(dimension, $"fill {fill.Min} {fill.Max} {state} strict");
            }

            var reader = new BoxReader(vanilla, dimension, box, StatesIn(scenario));
            var ticks = scenario["checkpoints"]!.AsArray()
                .Select(checkpoint => checkpoint!["tick"]!.GetValue<int>())
                .ToList();
            var actions = scenario["actions"]!.AsArray();
            var checkpoints = new JsonArray();
            for (var tick = 0; tick <= ticks.Max(); tick++)
            {
                foreach (var action in AtTick(actions, tick))
                    await SetBlockAsync(vanilla, dimension, ReadPosition(action["pos"]!), action["state"]!);

                if (tick > 0)
                    await vanilla.StepAsync();

                if (ticks.Contains(tick))
                    checkpoints.Add(await reader.ReadAsync(tick));
            }

            scenario["checkpoints"] = checkpoints;
            await ClearAsync(vanilla, dimension, area);
        }
    }

    // ---------------------------------------------------------------- daylight detector

    /// <summary>
    /// A detector and an inverted one on open sky. For each sample, the clock is set to the sample's time and the
    /// game steps to the next multiple of 20 game ticks, when detectors update; the reading holds the clock's time in
    /// that tick (vanilla ticks the clocks before the levels) and both detectors' power.
    /// </summary>
    private static async Task CaptureDaylightAsync(ScenarioServer vanilla, JsonObject root)
    {
        Console.WriteLine("Capturing daylight detector readings...");
        var dimension = ScenarioServer.Overworld;
        var detector = new Position(0, 64, 0);
        var inverted = new Position(2, 64, 0);
        var area = new Box(new Position(-1, 63, -1), new Position(3, 65, 1));
        await LoadAsync(vanilla, dimension, area);
        await ClearAsync(vanilla, dimension, area);
        await vanilla.RunInAsync(dimension, $"fill {area.Min} {area.Max.X} 63 {area.Max.Z} minecraft:stone strict");
        await vanilla.RunInAsync(dimension, $"setblock {detector} minecraft:daylight_detector[inverted=false]");
        await vanilla.RunInAsync(dimension, $"setblock {inverted} minecraft:daylight_detector[inverted=true]");

        var readings = new JsonArray();
        var samples = root["samples"]!.AsArray().Select(sample => sample!.GetValue<int>()).ToList();
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];

            // Samples set the clock at every phase of the update interval in turn, so the readings land 1 to 20 ticks
            // after their samples.
            var phase = index % UpdateInterval;
            while (await vanilla.GameTimeAsync() % UpdateInterval != phase)
                await vanilla.StepAsync();

            await vanilla.RunInAsync(dimension, $"time set {sample}");
            do
            {
                await vanilla.StepAsync();
            }
            while (await vanilla.GameTimeAsync() % UpdateInterval != 0);

            var reading = new JsonObject
            {
                ["sample"] = sample,
                ["dayTime"] = await vanilla.ClockTimeAsync(),
                ["power"] = await ReadPowerAsync(vanilla, dimension, detector),
                ["inverted"] = await ReadPowerAsync(vanilla, dimension, inverted),
            };
            readings.Add(reading);
        }

        root["readings"] = readings;
        await ClearAsync(vanilla, dimension, area);
    }

    private static async Task<int> ReadPowerAsync(ScenarioServer vanilla, string dimension, Position position)
    {
        var state = await vanilla.ReadStateAsync(dimension, position, null, ["minecraft:daylight_detector"]);
        var properties = state.Split('[', ',', ']');
        var power = properties.Single(property => property.StartsWith("power=", StringComparison.Ordinal));
        return int.Parse(power["power=".Length..]);
    }

    // ---------------------------------------------------------------- shared

    private static IEnumerable<JsonNode> AtTick(JsonArray events, int tick) =>
        events.Select(node => node!).Where(node => node["tick"]!.GetValue<int>() == tick);

    // Every state a scenario names, which its boxes are likely to hold.
    private static IEnumerable<string> StatesIn(JsonObject scenario)
    {
        var placements = scenario["setup"]!.AsArray().Concat(scenario["actions"]!.AsArray());
        return placements
            .Select(placement => placement!["state"]?.GetValue<string>())
            .OfType<string>()
            .Select(state => state.Split('[')[0]);
    }

    private static async Task SetBlockAsync(ScenarioServer vanilla, string dimension, Position position, JsonNode state)
    {
        // Fails (harmlessly) when the block already has the state.
        await vanilla.RunInAsync(dimension, $"setblock {position} {state.GetValue<string>()}");
    }

    // Force-loads the chunks around an area, which keeps them loaded and ticking across restarts.
    private static async Task LoadAsync(ScenarioServer vanilla, string dimension, Box area)
    {
        await vanilla.RunInAsync(dimension, $"forceload add {area.Min.X} {area.Min.Z} {area.Max.X} {area.Max.Z}");
    }

    // Clears an area without updates, then runs a few ticks so that what was scheduled there finds air.
    private static async Task ClearAsync(ScenarioServer vanilla, string dimension, Box area)
    {
        foreach (var slice in area.Slices())
            await vanilla.RunInAsync(dimension, $"fill {slice.Min} {slice.Max} minecraft:air strict");

        for (var tick = 0; tick < DrainTicks; tick++)
            await vanilla.StepAsync();
    }

    private static Position ReadPosition(JsonNode node)
    {
        var array = node.AsArray();
        return new Position(array[0]!.GetValue<int>(), array[1]!.GetValue<int>(), array[2]!.GetValue<int>());
    }

    /// <summary>An inclusive box of blocks.</summary>
    private readonly record struct Box(Position Min, Position Max)
    {
        // /fill changes at most this many blocks at once.
        private const int FillLimit = 32768;

        public static Box Read(JsonObject scenario) =>
            new(ReadPosition(scenario["min"]!), ReadPosition(scenario["max"]!));

        public Box Grow(int margin)
        {
            var min = this.Min.Offset(new Position(-margin, -margin, -margin));
            var max = this.Max.Offset(new Position(margin, margin, margin));
            return new Box(min, max);
        }

        public Box Union(Box other)
        {
            var min = new Position(
                Math.Min(this.Min.X, other.Min.X),
                Math.Min(this.Min.Y, other.Min.Y),
                Math.Min(this.Min.Z, other.Min.Z));
            var max = new Position(
                Math.Max(this.Max.X, other.Max.X),
                Math.Max(this.Max.Y, other.Max.Y),
                Math.Max(this.Max.Z, other.Max.Z));
            return new Box(min, max);
        }

        // Horizontal slices of the box that /fill can each change at once.
        public IEnumerable<Box> Slices()
        {
            var layer = (this.Max.X - this.Min.X + 1) * (this.Max.Z - this.Min.Z + 1);
            var layers = Math.Max(1, FillLimit / layer);
            for (var y = this.Min.Y; y <= this.Max.Y; y += layers)
            {
                var top = Math.Min(this.Max.Y, y + layers - 1);
                yield return new Box(this.Min with { Y = y }, this.Max with { Y = top });
            }
        }
    }

    /// <summary>
    /// Reads a scenario's box into a checkpoint: one string per (y, z) row, y then z ascending, with a character per
    /// x giving the block's index in the palette, which lists full states in the order the rows first use them. Each
    /// block is tried against its state at the previous checkpoint first.
    /// </summary>
    private sealed class BoxReader(ScenarioServer vanilla, string dimension, Box box, IEnumerable<string> likelyIds)
    {
        private readonly Dictionary<Position, string> previous = [];
        private readonly List<string> likely = [.. likelyIds.Concat(LikelyIds).Distinct()];

        public async Task<JsonObject> ReadAsync(int tick)
        {
            var palette = new List<string>();
            var rows = new JsonArray();
            for (var y = box.Min.Y; y <= box.Max.Y; y++)
            {
                for (var z = box.Min.Z; z <= box.Max.Z; z++)
                {
                    var row = new StringBuilder();
                    for (var x = box.Min.X; x <= box.Max.X; x++)
                    {
                        var state = await this.ReadAsync(new Position(x, y, z));
                        if (!palette.Contains(state))
                            palette.Add(state);

                        row.Append(Symbols[palette.IndexOf(state)]);
                    }

                    rows.Add(row.ToString());
                }
            }

            return new JsonObject
            {
                ["tick"] = tick,
                ["palette"] = new JsonArray([.. palette.Select(state => JsonValue.Create(state))]),
                ["rows"] = rows,
            };
        }

        private async Task<string> ReadAsync(Position position)
        {
            var expected = this.previous.GetValueOrDefault(position);
            var state = await vanilla.ReadStateAsync(dimension, position, expected, this.likely);
            this.previous[position] = state;

            var id = state.Split('[')[0];
            if (!this.likely.Contains(id))
                this.likely.Add(id);

            return state;
        }
    }
}
