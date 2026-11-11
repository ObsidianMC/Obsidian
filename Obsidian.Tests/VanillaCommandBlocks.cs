using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Utilities;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Redstone;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Replays vanilla's command blocks (<c>Assets/command_blocks.json</c>, recorded by <c>CommandBlockFixtures.java</c>)
/// in one shared world: each case's blocks and command blocks are set up without updates, then its actions run
/// before their ticks (blocks set with every update, and edits through <see cref="SetCommandBlockPacket"/> from an
/// operator in creative mode), and after every tick the area's changed blocks, each command block's fields and the
/// chat the operator received are compared with vanilla's; after the last tick, what each command block saves.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed partial class VanillaCommandBlocks(ITestOutputHelper output)
{
    // The compared area around the origin, as in the capture.
    private const int MinX = -2;
    private const int MaxX = 5;
    private const int MinY = -1;
    private const int MaxY = 3;
    private const int MinZ = -2;
    private const int MaxZ = 3;

    // Ticks run after a case's area is cleared, as in the capture.
    private const int DrainTicks = 3;

    // Setup and clearing, like the capture's: no neighbor or shape updates and nothing placed reacting.
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    // The game rules cases change, with the values every case starts from.
    private static readonly Dictionary<string, string> defaultRules = new()
    {
        ["command_block_output"] = "true",
        ["send_command_feedback"] = "true",
        ["command_blocks_work"] = "true",
        ["max_command_sequence_length"] = "65536"
    };

    private sealed record Placement(Vector Offset, IBlock State);

    private sealed record CommandBlockSetup(
        Vector Offset,
        IBlock State,
        string Command,
        bool TrackOutput,
        bool Automatic);

    private sealed record Edit(Vector Offset, string Command, int Mode, bool TrackOutput, bool Conditional,
        bool Automatic);

    private sealed record ScheduledAction(int Tick, Placement? Set, Edit? Edit);

    private sealed record Checkpoint(int Tick, string Changes, string[] CommandBlocks, string[] Chat);

    private sealed record Case(
        string Name,
        int Ticks,
        Dictionary<string, string> Rules,
        Placement[] Setup,
        CommandBlockSetup[] CommandBlocks,
        ScheduledAction[] Actions,
        Checkpoint[] Expected,
        string[] Saved);

    private sealed record Harness(RealServerContext Context, Vector Origin, ServerPlayer Operator);

    [Fact]
    public async Task ReplayCommandBlockReference()
    {
        using var stream = typeof(VanillaCommandBlocks).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.command_blocks.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var cases = root.GetProperty("cases").EnumerateArray().Select(ReadCase).ToArray();
        var harness = await StartAsync(Position(root.GetProperty("origin")));
        await using var context = harness.Context;

        await FixtureCases.AssertAllAsync(cases, scenario => scenario.Name, async scenario =>
        {
            var differences = await ReplayAsync(harness, scenario);
            Assert.True(differences.Count == 0, string.Join("; ", differences));
        });

        output.WriteLine($"Replayed {cases.Length} command block cases.");
    }

    // One world of empty chunks around the origin, kept ticking by an operator in creative mode beside it, who hears
    // the chat and edits the command blocks.
    private static async Task<Harness> StartAsync(Vector origin)
    {
        var context = TestServerFactory.CreateServer($"command-blocks-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";
        ((CommandHandler)context.Server.CommandHandler).RegisterCommands();

        var (chunkX, chunkZ) = (origin.X >> 4, origin.Z >> 4);
        for (var x = chunkX - 2; x <= chunkX + 2; x++)
        {
            for (var z = chunkZ - 2; z <= chunkZ + 2; z++)
                WorldBlockEntities.AddEmptyChunk(world, x, z);
        }

        for (var x = chunkX - 2; x <= chunkX + 2; x++)
        {
            for (var z = chunkZ - 2; z <= chunkZ + 2; z++)
                await world.GetChunkAsync(x, z, scheduleGeneration: false);
        }

        var standing = (VectorD)origin + new VectorD(0.5, 0, -6.5);
        var operatorPlayer = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Operator", standing);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, operatorPlayer);
        for (var x = chunkX - 2; x <= chunkX + 2; x++)
        {
            for (var z = chunkZ - 2; z <= chunkZ + 2; z++)
                operatorPlayer.LoadedChunks.Add(NumericsHelper.IntsToLong(x, z));
        }

        MakeOperator(context.Server, operatorPlayer);
        operatorPlayer.GameMode = GameMode.Creative;
        return new Harness(context, origin, operatorPlayer);
    }

    // Fills the operator list in memory, without writing ops.json in the developer's configuration directory.
    private static void MakeOperator(Server server, ServerPlayer player)
    {
        var field = typeof(OperatorList).GetField("operators", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var entries = (IList)field.GetValue(server.Operators)!;
        var type = entries.GetType().GetGenericArguments()[0];
        var entry = Activator.CreateInstance(type)!;
        type.GetProperty("Uuid")!.SetValue(entry, player.Uuid);
        type.GetProperty("Username")!.SetValue(entry, player.Username);
        type.GetProperty("Level")!.SetValue(entry, 4);
        entries.Add(entry);
    }

    private static async Task<List<string>> ReplayAsync(Harness harness, Case scenario)
    {
        var world = harness.Context.World;

        // A cleared area, whose pending ticks find air and are dropped, as in the capture.
        Clear(harness);
        for (var drain = 0; drain < DrainTicks; drain++)
            await world.DoWorldTickAsync();

        foreach (var (rule, value) in defaultRules)
            world.LevelData.GameRules[rule] = scenario.Rules.GetValueOrDefault(rule, value);

        SetUp(harness, scenario);
        var before = Snapshot(harness);
        var start = ((IRedstoneLevel)world).GameTime;
        Forget(harness);

        var differences = new List<string>();
        foreach (var expected in scenario.Expected)
        {
            await ActAsync(harness, scenario, expected.Tick);
            await world.DoWorldTickAsync();
            var actual = Record(harness, expected.Tick, before);
            differences.AddRange(Compare(expected, actual));
        }

        var saved = Saved(harness, start);
        if (!saved.SequenceEqual(scenario.Saved))
        {
            var wanted = string.Join(" | ", scenario.Saved);
            var got = string.Join(" | ", saved);
            differences.Add($"saved: expected [{wanted}], got [{got}]");
        }

        return differences;
    }

    // The case's blocks, then its command blocks with their fields, the way loading their data sets them: always
    // active last, which schedules an always active block's first tick.
    private static void SetUp(Harness harness, Case scenario)
    {
        var world = harness.Context.World;
        foreach (var setup in scenario.Setup)
            world.BlockUpdates.SetBlock(harness.Origin + setup.Offset, setup.State, Quiet);

        foreach (var block in scenario.CommandBlocks)
        {
            var position = harness.Origin + block.Offset;
            world.BlockUpdates.SetBlock(position, block.State, Quiet);

            var entity = Assert.IsType<CommandBlockEntity>(world.GetResidentBlockEntity(position));
            entity.Command.SetCommand(block.Command);
            entity.Command.TrackOutput = block.TrackOutput;
            CommandBlocks.SetAutomatic(world, position, block.State, entity, block.Automatic);
        }
    }

    // The actions due before the tick: a block set with every update, or an edit through the editor's packet.
    private static async Task ActAsync(Harness harness, Case scenario, int tick)
    {
        var world = harness.Context.World;
        foreach (var action in scenario.Actions.Where(action => action.Tick == tick))
        {
            if (action.Set is Placement set)
            {
                await world.SetBlockAsync(harness.Origin + set.Offset, set.State, doBlockUpdate: true);
                continue;
            }

            var edit = action.Edit!;
            var packet = EditPacket(harness.Origin + edit.Offset, edit);
            await packet.HandleAsync(harness.Context.Server, harness.Operator);
        }
    }

    // The packet an editor sends, read from its bytes as the server reads it.
    private static SetCommandBlockPacket EditPacket(Vector position, Edit edit)
    {
        using var buffer = new NetworkBuffer();
        buffer.WritePosition(position);
        buffer.WriteString(edit.Command);
        buffer.WriteVarInt(edit.Mode);

        var flags = (edit.TrackOutput ? 1 : 0) | (edit.Conditional ? 2 : 0) | (edit.Automatic ? 4 : 0);
        buffer.WriteByte((byte)flags);

        return SetCommandBlockPacket.Deserialize(buffer.AsSpan(0, buffer.Size).ToArray());
    }

    // What one tick did, the way the capture writes it.
    private static Checkpoint Record(Harness harness, int tick, IBlock[] before)
    {
        var world = harness.Context.World;
        var blocks = Area(harness)
            .Select(position => (position, world.GetResidentBlockEntity(position)))
            .Where(pair => pair.Item2 is CommandBlockEntity)
            .Select(pair => Describe(harness, pair.position, (CommandBlockEntity)pair.Item2!))
            .ToArray();

        var chat = ((TestClient)harness.Operator.Client).SentPackets
            .OfType<SystemChatPacket>()
            .Where(packet => !packet.Overlay)
            .Select(packet => StoredCommand.PlainText(packet.Message))
            .ToArray();
        Forget(harness);

        return new Checkpoint(tick, Changes(harness, before), blocks, chat);
    }

    // "x,y,z success=N powered=b conditionMet=b auto=b output=<text>", the output without its time; "-" for none.
    private static string Describe(Harness harness, Vector position, CommandBlockEntity entity)
    {
        var command = entity.Command;
        var output = Output(command.LastOutput);
        var powered = Lower(entity.Powered);
        var met = Lower(entity.ConditionMet);
        var automatic = Lower(entity.Automatic);
        return $"{Offset(harness, position)} success={command.SuccessCount} powered={powered} "
            + $"conditionMet={met} auto={automatic} output={output}";
    }

    // Each command block's saved fields, sorted: "x,y,z Key=value", times relative to the game time before the
    // first tick.
    private static string[] Saved(Harness harness, long start)
    {
        var world = harness.Context.World;
        var saved = new List<string>();
        foreach (var position in Area(harness))
        {
            if (world.GetResidentBlockEntity(position) is not CommandBlockEntity entity)
                continue;

            var fields = entity.EditorData()
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={SavedValue(pair.Key, pair.Value, start)}");
            saved.Add($"{Offset(harness, position)} {string.Join(' ', fields)}");
        }

        return [.. saved];
    }

    private static string SavedValue(string key, INbtTag tag, long start) => tag switch
    {
        _ when key is "LastOutput" or "CustomName" => Output(tag.TextFromNbt()),
        NbtTag<long> time when key == "LastExecution" => (time.Value - start).ToString(),
        NbtTag<bool> flag => flag.Value ? "1b" : "0b",
        NbtTag<byte> flag => $"{flag.Value}b",
        NbtTag<int> number => number.Value.ToString(),
        NbtTag<string> text => text.Value ?? string.Empty,
        _ => tag.ToString() ?? string.Empty
    };

    // A command block's output as the capture writes it: its text without the time before it, or "-". Vanilla's
    // outputs all start with the time but Searge's easter egg, so one without it is marked and differs.
    private static string Output(ChatMessage? message)
    {
        if (message is null)
            return "-";

        var text = StoredCommand.PlainText(message);
        if (!TimePrefix().IsMatch(text) && text != SeargeOutput)
            return $"untimed {text}";

        var untimed = TimePrefix().Replace(text, string.Empty, 1);
        return untimed.Length == 0 ? "-" : untimed;
    }

    // What vanilla's easter egg says, without a time.
    private const string SeargeOutput = "#itzlipofutzli";

    [GeneratedRegex(@"^\[\d\d:\d\d:\d\d\] ")]
    private static partial Regex TimePrefix();

    private static string Lower(bool value) => value ? "true" : "false";

    private static IEnumerable<string> Compare(Checkpoint expected, Checkpoint actual)
    {
        var tick = $"tick {expected.Tick}";
        if (expected.Changes != actual.Changes)
            yield return $"{tick} changes: expected '{expected.Changes}', got '{actual.Changes}'";

        if (!expected.CommandBlocks.SequenceEqual(actual.CommandBlocks))
        {
            var wanted = string.Join(" | ", expected.CommandBlocks);
            var got = string.Join(" | ", actual.CommandBlocks);
            yield return $"{tick} command blocks: expected [{wanted}], got [{got}]";
        }

        if (!expected.Chat.SequenceEqual(actual.Chat))
        {
            var wanted = string.Join(" | ", expected.Chat);
            var got = string.Join(" | ", actual.Chat);
            yield return $"{tick} chat: expected [{wanted}], got [{got}]";
        }
    }

    private static void Forget(Harness harness) => ((TestClient)harness.Operator.Client).ClearPackets();

    private static void Clear(Harness harness)
    {
        var world = harness.Context.World;
        for (var y = MinY - 1; y <= MaxY + 1; y++)
        {
            for (var z = MinZ - 1; z <= MaxZ + 1; z++)
            {
                for (var x = MinX - 1; x <= MaxX + 1; x++)
                    world.BlockUpdates.SetBlock(harness.Origin + new Vector(x, y, z), BlocksRegistry.Air, Quiet);
            }
        }
    }

    private static IBlock[] Snapshot(Harness harness) =>
        Area(harness).Select(harness.Context.World.BlockUpdates.GetBlock).ToArray();

    // Each block of the area that changed, as dx,dy,dz=state relative to the origin.
    private static string Changes(Harness harness, IBlock[] before)
    {
        var world = harness.Context.World;
        var changed = new List<string>();
        var index = 0;
        foreach (var position in Area(harness))
        {
            var state = world.BlockUpdates.GetBlock(position);
            if (!state.IsSameState(before[index]))
                changed.Add($"{Offset(harness, position)}={Text(state)}");

            index++;
        }

        return string.Join(';', changed);
    }

    // The compared area in the capture's order: y, then z, then x ascending.
    private static IEnumerable<Vector> Area(Harness harness)
    {
        for (var y = MinY; y <= MaxY; y++)
        {
            for (var z = MinZ; z <= MaxZ; z++)
            {
                for (var x = MinX; x <= MaxX; x++)
                    yield return harness.Origin + new Vector(x, y, z);
            }
        }
    }

    private static string Offset(Harness harness, Vector position)
    {
        var offset = position - harness.Origin;
        return $"{offset.X},{offset.Y},{offset.Z}";
    }

    // The block and the properties it doesn't share with its default state, sorted by name like vanilla's.
    private static string Text(IBlock state)
    {
        var defaults = BlockStateProperties.GetState(state.UnlocalizedName).GetProperties();
        var properties = state.GetProperties()
            .Where(pair => defaults.GetValueOrDefault(pair.Key) != pair.Value)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToArray();

        if (properties.Length == 0)
            return state.UnlocalizedName;

        return $"{state.UnlocalizedName}[{string.Join(',', properties)}]";
    }

    private static Case ReadCase(JsonElement input)
    {
        var rules = new Dictionary<string, string>();
        if (input.TryGetProperty("rules", out var rulesElement))
        {
            foreach (var rule in rulesElement.EnumerateObject())
                rules[rule.Name] = rule.Value.GetString()!;
        }

        var setup = Array(input, "setup").Select(ReadPlacement).ToArray();
        var blocks = Array(input, "commandBlocks").Select(ReadCommandBlock).ToArray();
        var actions = Array(input, "actions").Select(ReadAction).ToArray();
        var ticks = input.GetProperty("ticks").GetInt32();
        var checkpoints = input.GetProperty("checkpoints").EnumerateArray().Select(ReadCheckpoint).ToArray();
        Assert.Equal(ticks, checkpoints.Length);

        return new Case(
            input.GetProperty("name").GetString()!,
            ticks,
            rules,
            setup,
            blocks,
            actions,
            checkpoints,
            Strings(input, "saved"));
    }

    private static Placement ReadPlacement(JsonElement element) => new(
        Position(element.GetProperty("offset")),
        VanillaSulfurBlocks.ParseState(element.GetProperty("state").GetString()!));

    private static CommandBlockSetup ReadCommandBlock(JsonElement element) => new(
        Position(element.GetProperty("offset")),
        VanillaSulfurBlocks.ParseState(element.GetProperty("state").GetString()!),
        element.GetProperty("command").GetString()!,
        Flag(element, "trackOutput", fallback: true),
        Flag(element, "auto", fallback: false));

    private static ScheduledAction ReadAction(JsonElement element)
    {
        var tick = element.GetProperty("tick").GetInt32();
        if (!element.TryGetProperty("edit", out var edit))
            return new ScheduledAction(tick, ReadPlacement(element), null);

        var mode = edit.GetProperty("mode").GetString() switch
        {
            "sequence" => CommandBlockMode.Sequence,
            "auto" => CommandBlockMode.Auto,
            _ => CommandBlockMode.Redstone
        };

        var read = new Edit(
            Position(edit.GetProperty("offset")),
            edit.GetProperty("command").GetString()!,
            (int)mode,
            Flag(edit, "trackOutput", fallback: true),
            Flag(edit, "conditional", fallback: false),
            Flag(edit, "auto", fallback: false));
        return new ScheduledAction(tick, null, read);
    }

    private static Checkpoint ReadCheckpoint(JsonElement checkpoint) => new(
        checkpoint.GetProperty("tick").GetInt32(),
        checkpoint.TryGetProperty("changes", out var changes) ? changes.GetString()! : "",
        Strings(checkpoint, "commandBlocks"),
        Strings(checkpoint, "chat"));

    private static bool Flag(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var value) ? value.GetBoolean() : fallback;

    private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out var values) ? values.EnumerateArray() : [];

    private static string[] Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var values)
            ? [.. values.EnumerateArray().Select(value => value.GetString()!)]
            : [];

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());
}
