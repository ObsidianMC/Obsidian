using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.AssetGenerator;

/// <summary>
/// Re-records <c>Obsidian.Tests/Assets/block_contact_scenarios.json</c>, the tick-by-tick vanilla scenarios of entities
/// touching blocks, replayed by <c>VanillaBlockContact</c>. Original command-driven cases use
/// <see cref="ScenarioServer"/>; controlled native moves use <c>Java/BlockContactFixtures.java</c>.
/// </summary>
/// <remarks>
/// The fixture is its own input: each scenario's box, setup and entity events are kept, and only its checkpoints are
/// replaced. Entities are summoned, teleported, removed and killed with commands between ticks, carrying the tag
/// their events name, and every checkpoint records the box's blocks and where each of the scenario's entities is.
/// </remarks>
internal static class ScenarioContactCaptures
{
    public const string Fixture = "block_contact";

    private const string FileName = "block_contact_scenarios.json";

    private const string Symbols = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // Every scenario entity carries this tag besides its own, so that all of them can be removed at once.
    private const string ScenarioTag = "contact_scenario";

    // Ticks run after a scenario's blocks are cleared, so that work it left scheduled finds air.
    private const int DrainTicks = 40;

    private static readonly string[] LikelyIds = ["minecraft:air", "minecraft:redstone_wire", "minecraft:stone"];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 1,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string Notes = """
        Vanilla entities touching blocks, recorded by the asset generator's capture-scenarios
        (ScenarioContactCaptures.cs) on a headless vanilla server in a void world, whose game is frozen and stepped one
        tick at a time, with random ticks, mob spawning and weather off. Coordinates are absolute. Setup blocks are
        placed first, live, in order, with neighbour updates (/setblock). Each tick t from 1 to the scenario's ticks:
        its entity events happen first, at the previous game time, then the game advances one tick. An event
        summons an entity of a type at a position (an item holding `item`, with a pickup delay that never runs out; a
        mob with `noAi`; with `noGravity`, for an entity the events move a step each tick; with `motion` as its
        starting velocity), teleports one (`move`), removes one (`remove`:
        moved far below and killed, so it leaves at once), or kills one in place (`kill`: a mob then dies as usual,
        staying in the level for its death animation). Checkpoints hold the box after each tick (checkpoint 0 after
        setup): one string per (y, z) row, y then z ascending, with a character per x giving the block's palette index
        in 0-9a-zA-Z; palette states are full states with every property, sorted by name. `entities` holds where each
        of the scenario's entities still in the level is, by tag.
        """;

    /// <summary>
    /// Captures every scenario of the fixture, or with <paramref name="cases"/> only the ones it names.
    /// </summary>
    public static async Task RunAsync(VanillaServer server, string assetsDirectory,
        IReadOnlyCollection<string>? cases)
    {
        var path = Path.Combine(assetsDirectory, FileName);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();

        var scenarios = root["scenarios"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        var selected = scenarios
            .Where(scenario => cases is null || cases.Contains(scenario["name"]!.GetValue<string>()))
            .ToArray();
        if (cases is not null && selected.Length != cases.Count)
            throw new ArgumentException("Every selected block contact scenario must exist and be named once.");

        var commandCases = selected.Where(scenario => scenario["contactMoves"] is null).ToArray();
        if (commandCases.Length > 0)
        {
            await using var vanilla = await ScenarioServer.StartAsync(server, null);
            await PrepareAsync(vanilla);
            foreach (var scenario in commandCases)
                await CaptureAsync(vanilla, scenario);
        }

        root["_notes"] = Notes.ReplaceLineEndings(" ");
        root["version"] = server.Version;
        var text = root.ToJsonString(WriteOptions) + "\n";
        await File.WriteAllTextAsync(path, text.ReplaceLineEndings("\n"), new UTF8Encoding(false));
        var moveCases = selected.Where(scenario => scenario["contactMoves"] is not null)
            .Select(scenario => scenario["name"]!.GetValue<string>()).ToArray();
        if (moveCases.Length > 0)
        {
            await FixtureDumper.DumpBlockContactsAsync(server, path, moveCases);
            var captured = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var formatted = captured.ToJsonString(WriteOptions) + "\n";
            await File.WriteAllTextAsync(path, formatted.ReplaceLineEndings("\n"), new UTF8Encoding(false));
        }

        Console.WriteLine($"Captured {FileName}.");
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

    private static async Task CaptureAsync(ScenarioServer vanilla, JsonObject scenario)
    {
        const string dimension = ScenarioServer.Overworld;
        var name = scenario["name"]!.GetValue<string>();
        Console.WriteLine($"Capturing block contact scenario {name}...");

        var min = ReadPosition(scenario["min"]!);
        var max = ReadPosition(scenario["max"]!);
        var ticks = scenario["ticks"]!.GetValue<int>();
        var events = scenario["entities"]!.AsArray().Select(node => node!).ToArray();
        var tags = events
            .Select(entry => entry["summon"]?.GetValue<string>())
            .OfType<string>()
            .ToArray();

        await vanilla.RunInAsync(dimension, $"forceload add {min.X - 2} {min.Z - 2} {max.X + 2} {max.Z + 2}");
        await ClearAsync(vanilla, dimension, min, max);

        foreach (var placement in scenario["setup"]!.AsArray())
        {
            var position = ReadPosition(placement!["pos"]!);
            var state = placement["state"]!.GetValue<string>();
            await vanilla.RunInAsync(dimension, $"setblock {position} {state}");
        }

        var reader = new BoxReader(vanilla, dimension, min, max);
        var checkpoints = new JsonArray { await ReadCheckpointAsync(vanilla, reader, tags, 0) };
        for (var tick = 1; tick <= ticks; tick++)
        {
            foreach (var entry in events.Where(entry => entry["tick"]!.GetValue<int>() == tick))
                await RunEventAsync(vanilla, dimension, entry);

            await vanilla.StepAsync();
            checkpoints.Add(await ReadCheckpointAsync(vanilla, reader, tags, tick));
        }

        scenario["checkpoints"] = checkpoints;
        await RemoveAsync(vanilla, dimension, ScenarioTag);
        await vanilla.RunInAsync(dimension, $"kill @e[type=minecraft:item,{Volume(min, max)}]");
        await ClearAsync(vanilla, dimension, min, max);
    }

    private static async Task RunEventAsync(ScenarioServer vanilla, string dimension, JsonNode entry)
    {
        if (entry["summon"] is JsonNode summon)
        {
            var tag = summon.GetValue<string>();
            var position = ReadLocation(entry["pos"]!);
            var type = entry["type"]!.GetValue<string>();
            var data = SummonData(entry, tag);
            var command = $"summon {type} {Coordinates(position)} {data}";
            await vanilla.RunInAsync(dimension, command);
        }
        else if (entry["move"] is JsonNode move)
        {
            var position = ReadLocation(entry["pos"]!);
            var target = $"@e[tag={move.GetValue<string>()},limit=1]";
            var command = $"tp {target} {Coordinates(position)}";
            await vanilla.RunInAsync(dimension, command);
        }
        else if (entry["remove"] is JsonNode remove)
        {
            await RemoveAsync(vanilla, dimension, remove.GetValue<string>());
        }
        else if (entry["kill"] is JsonNode kill)
        {
            await vanilla.RunInAsync(dimension, $"kill @e[tag={kill.GetValue<string>()}]");
        }
        else
        {
            throw new InvalidDataException($"Unknown entity event {entry.ToJsonString()}.");
        }
    }

    // The summoned entity's NBT: its tags, an item's stack and endless pickup delay, a mob's NoAI, no gravity for an
    // entity the events move, and a velocity.
    private static string SummonData(JsonNode entry, string tag)
    {
        var parts = new List<string> { $"Tags:[\"{ScenarioTag}\",\"{tag}\"]" };
        if (entry["item"] is JsonNode item)
        {
            parts.Add($"Item:{{id:\"{item.GetValue<string>()}\",count:1}}");
            parts.Add("PickupDelay:32767s");
        }

        if (entry["noAi"]?.GetValue<bool>() == true)
            parts.Add("NoAI:1b");

        if (entry["noGravity"]?.GetValue<bool>() == true)
            parts.Add("NoGravity:1b");

        if (entry["motion"] is JsonNode motion)
        {
            var values = ReadLocation(motion).Select(value => $"{Number(value)}d");
            parts.Add($"Motion:[{string.Join(",", values)}]");
        }

        return $"{{{string.Join(",", parts)}}}";
    }

    // Moved below the world before they're killed, so they leave at once and nothing they drop lands in the box.
    private static async Task RemoveAsync(ScenarioServer vanilla, string dimension, string tag)
    {
        await vanilla.RunInAsync(dimension, $"tp @e[tag={tag}] 0 -1000 0");
        await vanilla.RunInAsync(dimension, $"kill @e[tag={tag}]");
    }

    private static async Task<JsonObject> ReadCheckpointAsync(ScenarioServer vanilla, BoxReader reader,
        IReadOnlyList<string> tags, int tick)
    {
        var checkpoint = await reader.ReadAsync(tick);
        var entities = new JsonObject();
        foreach (var tag in tags)
        {
            if (await ReadEntityPositionAsync(vanilla, tag) is double[] position)
                entities[tag] = new JsonArray([.. position.Select(value => JsonValue.Create(value))]);
        }

        checkpoint["entities"] = entities;
        return checkpoint;
    }

    // The entity's position, or null once it has left the level (or was moved below the world to leave it).
    private static async Task<double[]?> ReadEntityPositionAsync(ScenarioServer vanilla, string tag)
    {
        var feedback = await vanilla.RunAsync(
            $"execute in {ScenarioServer.Overworld} run data get entity @e[tag={tag},limit=1] Pos");
        var start = feedback.IndexOf('[');
        var end = feedback.IndexOf(']');
        if (start < 0 || end < start)
            return null;

        var values = feedback[(start + 1)..end]
            .Split(',')
            .Select(value => double.Parse(value.Trim().TrimEnd('d'), CultureInfo.InvariantCulture))
            .ToArray();
        return values[1] < -500 ? null : values;
    }

    // Clears the box and a block around it without updates, then lets what was scheduled there find air.
    private static async Task ClearAsync(ScenarioServer vanilla, string dimension, Position min, Position max)
    {
        var from = min.Offset(new Position(-1, -1, -1));
        var to = max.Offset(new Position(1, 1, 1));
        await vanilla.RunInAsync(dimension, $"fill {from} {to} minecraft:air strict");
        for (var tick = 0; tick < DrainTicks; tick++)
            await vanilla.StepAsync();
    }

    // Each coordinate with a decimal point: vanilla centres a whole x or z (429 is read as 429.5).
    private static string Coordinates(double[] position) =>
        $"{Number(position[0])} {Number(position[1])} {Number(position[2])}";

    private static string Number(double value) => value.ToString("0.0###############", CultureInfo.InvariantCulture);

    private static string Volume(Position min, Position max) =>
        $"x={min.X},y={min.Y},z={min.Z},dx={max.X - min.X},dy={max.Y - min.Y},dz={max.Z - min.Z}";

    private static Position ReadPosition(JsonNode node)
    {
        var array = node.AsArray();
        return new Position(array[0]!.GetValue<int>(), array[1]!.GetValue<int>(), array[2]!.GetValue<int>());
    }

    private static double[] ReadLocation(JsonNode node) =>
        node.AsArray().Select(value => value!.GetValue<double>()).ToArray();

    /// <summary>
    /// Reads the box into a checkpoint: one string per (y, z) row, y then z ascending, with a character per x giving
    /// the block's index in the palette. Each block is tried against its state at the previous checkpoint first.
    /// </summary>
    private sealed class BoxReader(ScenarioServer vanilla, string dimension, Position min, Position max)
    {
        private readonly Dictionary<Position, string> previous = [];
        private readonly List<string> likely = [.. LikelyIds];

        public async Task<JsonObject> ReadAsync(int tick)
        {
            var palette = new List<string>();
            var rows = new JsonArray();
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var z = min.Z; z <= max.Z; z++)
                {
                    var row = new StringBuilder();
                    for (var x = min.X; x <= max.X; x++)
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
