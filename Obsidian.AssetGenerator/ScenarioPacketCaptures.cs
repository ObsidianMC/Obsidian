using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.AssetGenerator;

/// <summary>
/// Re-records <c>Obsidian.Tests/Assets/block_change_packets.json</c>: the block and biome change packets vanilla sends
/// a player, in the order the player receives them, replayed by <c>VanillaBlockChangePackets</c>.
/// </summary>
/// <remarks>
/// The fixture is its own input: each case's fills, held item, actions and ticks are kept, and only its segments are
/// replaced. A <see cref="ScenarioClient"/> is the player; the server runs frozen, stepping one tick at a time.
/// </remarks>
internal static class ScenarioPacketCaptures
{
    public const string Fixture = "block_change_packets";

    private const string FileName = "block_change_packets.json";
    private const string PlayerName = "Probe";

    // Ticks run after a case, unrecorded, so that work it left scheduled is done before the next one.
    private const int DrainTicks = 20;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 1,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] Faces = ["down", "up", "north", "south", "west", "east"];

    private const string Notes = """
        The block change packets a vanilla player receives, in the order received, recorded by the asset generator's
        capture-scenarios (ScenarioPacketCaptures.cs, ScenarioClient.cs): a headless client logged in to a headless
        vanilla server in a void world, standing at `player` in creative mode, with random ticks, mob spawning and
        weather off and `chunks` force-loaded and sent to it. The game is frozen and stepped one tick at a time; a
        frozen server still ticks, and each of its ticks sends the block changes made since the last (vanilla's chunk
        broadcast) and then the prediction acknowledgements. Setup fills place blocks without updates (/fill strict),
        and `hand` is put in the player's main hand. Each tick t from 0 to `ticks`: for t > 0, the game steps one tick;
        then the tick's actions run: a live /setblock (with updates), a live /fillbiome (from, to, biome and an
        optional replace filter), or the client using its main hand's item on a block's face (clicking the face's
        centre) or starting to break a block, each with the prediction sequence it names; the client's own sequences
        for waiting on ticks start at 1000000 and aren't recorded. Segments hold what the client received after each
        step (`after: tick`) and after each tick's actions (`after: actions`), up to the first server tick after it:
        block updates, section updates (with their body's bytes), block events, chunk biome updates (each chunk's
        position and biome data bytes) and acknowledgements; an actions segment with a /fillbiome also holds the
        command's `feedback`, in order. States are full states with every property, sorted by name; coordinates are
        absolute. Biome changes stay in the world for the later cases.
        """;

    public static async Task RunAsync(VanillaServer server, string assetsDirectory, IReadOnlyCollection<string>? cases)
    {
        var path = Path.Combine(assetsDirectory, FileName);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var selected = root["cases"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(scenario => cases is null || cases.Contains(scenario["name"]!.GetValue<string>()))
            .ToArray();
        if (cases is not null && selected.Length != cases.Count)
            throw new ArgumentException("Every selected block change packet case must exist and be named once.");

        await using var vanilla = await ScenarioServer.StartAsync(server);
        await PrepareAsync(vanilla);

        await using var client = await ScenarioClient.JoinAsync(server, vanilla.GamePort, PlayerName);
        await vanilla.RunAsync($"gamemode creative {PlayerName}");

        var player = root["player"]!.AsArray().Select(value => value!.GetValue<double>()).ToArray();
        // Invariant: under a locale like de-DE, 8.5 would be written 8,5 and the teleport would fail.
        await vanilla.RunAsync(FormattableString.Invariant($"tp {PlayerName} {player[0]} {player[1]} {player[2]}"));
        await client.WaitForPositionAsync(player[0], player[1], player[2]);

        var chunks = root["chunks"]!.AsArray()
            .Select(chunk => (chunk![0]!.GetValue<int>(), chunk[1]!.GetValue<int>()))
            .ToArray();
        await client.WaitForChunksAsync(chunks);

        foreach (var scenario in selected)
            await CaptureAsync(vanilla, client, scenario);

        root["_notes"] = Notes.ReplaceLineEndings(" ").Trim();
        root["version"] = server.Version;
        var text = root.ToJsonString(WriteOptions) + "\n";
        await File.WriteAllTextAsync(path, text.ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
        Console.WriteLine($"Captured {FileName}.");
    }

    // Nothing but the cases changes the world: no random ticks, mobs or weather, and the cases' chunks always tick.
    private static async Task PrepareAsync(ScenarioServer vanilla)
    {
        string[] commands =
        [
            "gamerule minecraft:random_tick_speed 0",
            "gamerule minecraft:spawn_mobs false",
            "gamerule minecraft:advance_weather false",
            "weather clear",
            "forceload add -32 -32 47 47",
        ];
        foreach (var command in commands)
            await vanilla.RunInAsync(ScenarioServer.Overworld, command);
    }

    private static async Task CaptureAsync(ScenarioServer vanilla, ScenarioClient client, JsonObject scenario)
    {
        var name = scenario["name"]!.GetValue<string>();
        Console.WriteLine($"Capturing block change packet case {name}...");

        foreach (var fill in scenario["fills"]!.AsArray())
        {
            var from = Position(fill!["from"]!);
            var to = Position(fill["to"]!);
            var state = fill["state"]!.GetValue<string>();
            await vanilla.RunInAsync(ScenarioServer.Overworld, $"fill {from} {to} {state} strict");
        }

        if (scenario["hand"]?.GetValue<string>() is string item)
            await vanilla.RunAsync($"item replace entity {PlayerName} weapon.mainhand with {item}");

        // What the setup sent isn't part of the case.
        await client.SyncAsync();
        client.TakeRecorded();

        var actions = scenario["actions"]!.AsArray().Select(action => action!.AsObject()).ToArray();
        var segments = new JsonArray();
        var ticks = scenario["ticks"]!.GetValue<int>();
        for (var tick = 0; tick <= ticks; tick++)
        {
            if (tick > 0)
            {
                await vanilla.StepAsync();
                await client.SyncAsync();
                segments.Add(Segment(tick, "tick", client.TakeRecorded()));
            }

            var tickActions = actions.Where(action => action["tick"]!.GetValue<int>() == tick).ToArray();
            if (tickActions.Length == 0)
                continue;

            var feedback = new JsonArray();
            foreach (var action in tickActions)
            {
                if (await PerformAsync(vanilla, client, action) is string text)
                    feedback.Add(text);
            }

            var segment = Segment(tick, "actions", client.TakeRecorded());
            if (feedback.Count > 0)
                segment["feedback"] = feedback;

            segments.Add(segment);
        }

        scenario["segments"] = segments;

        for (var tick = 0; tick < DrainTicks; tick++)
            await vanilla.StepAsync();

        await client.SyncAsync();
        client.TakeRecorded();
    }

    // An action, and the first server tick after it. Returns a command's feedback, for those whose feedback is kept.
    private static async Task<string?> PerformAsync(ScenarioServer vanilla, ScenarioClient client, JsonObject action)
    {
        if (action["setblock"] is JsonObject setblock)
        {
            var position = Position(setblock["pos"]!);
            var state = setblock["state"]!.GetValue<string>();
            await vanilla.RunInAsync(ScenarioServer.Overworld, $"setblock {position} {state}");
            await client.SyncAsync();
            return null;
        }

        if (action["fillbiome"] is JsonObject fillbiome)
        {
            var feedback = await vanilla.RunInAsync(ScenarioServer.Overworld, FillBiomeCommand(fillbiome));
            await client.SyncAsync();
            return feedback;
        }

        if (action["use"] is JsonObject use)
        {
            var position = Position(use["pos"]!);
            var face = Face(use["face"]!);
            var cursor = Cursor(face);
            var sequence = use["sequence"]!.GetValue<int>();
            await client.UseItemOnAsync(position.X, position.Y, position.Z, face, cursor, sequence);
            return null;
        }

        if (action["break"] is JsonObject breaking)
        {
            var position = Position(breaking["pos"]!);
            var face = Face(breaking["face"]!);
            var sequence = breaking["sequence"]!.GetValue<int>();
            await client.StartBreakingAsync(position.X, position.Y, position.Z, face, sequence);
            return null;
        }

        throw new InvalidOperationException($"Unknown action {action.ToJsonString()}.");
    }

    // The /fillbiome a fillbiome action runs, without its slash.
    private static string FillBiomeCommand(JsonObject fillbiome)
    {
        var from = Position(fillbiome["from"]!);
        var to = Position(fillbiome["to"]!);
        var biome = fillbiome["biome"]!.GetValue<string>();
        var command = $"fillbiome {from} {to} {biome}";
        return fillbiome["filter"]?.GetValue<string>() is string filter ? $"{command} replace {filter}" : command;
    }

    private static JsonObject Segment(int tick, string after, List<ScenarioClient.RecordedPacket> packets)
    {
        var recorded = new JsonArray();
        foreach (var packet in packets)
        {
            var value = new JsonObject { ["type"] = packet.Kind };
            foreach (var property in packet.Value.EnumerateObject())
                value[property.Name] = JsonNode.Parse(property.Value.GetRawText());

            recorded.Add(value);
        }

        return new JsonObject
        {
            ["tick"] = tick,
            ["after"] = after,
            ["packets"] = recorded,
        };
    }

    private static int Face(JsonNode face) => Array.IndexOf(Faces, face.GetValue<string>());

    // The centre of a block's face, relative to the block.
    private static (float X, float Y, float Z) Cursor(int face) => face switch
    {
        0 => (0.5f, 0, 0.5f),
        1 => (0.5f, 1, 0.5f),
        2 => (0.5f, 0.5f, 0),
        3 => (0.5f, 0.5f, 1),
        4 => (0, 0.5f, 0.5f),
        _ => (1, 0.5f, 0.5f),
    };

    private static Position Position(JsonNode array) =>
        new(array[0]!.GetValue<int>(), array[1]!.GetValue<int>(), array[2]!.GetValue<int>());
}
