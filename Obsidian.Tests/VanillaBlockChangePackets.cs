using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Modules;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>Assets/block_change_packets.json</c>: the block updates, section updates, block events, chunk biome
/// updates and prediction acknowledgements vanilla sends a player, in the order it sends them, and what
/// <c>/fillbiome</c> says back.
/// </summary>
/// <remarks>
/// The capture's server is frozen and stepped; here the server is paused, its analog. Vanilla's frozen server still
/// ticks, sending the block changes made since its last tick and then the acknowledgements; a paused server's tick
/// loop does the same (<see cref="Server.RunTickTurnAsync"/>). A step is a world tick, and each segment ends, like the
/// capture's, with the first frozen tick after it: one paused turn of the tick loop.
/// </remarks>
public sealed class VanillaBlockChangePackets
{
    private const string FixtureResource = "Obsidian.Tests.Assets.block_change_packets.json";

    // Ticks run after a case, unrecorded, as the capture does.
    private const int DrainTicks = 20;

    private static readonly string[] Faces = ["down", "up", "north", "south", "west", "east"];

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task VanillaPacketOrderReplays()
    {
        using var document = LoadFixture();
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        await using var context = TestServerFactory.CreateServer($"block-change-packets-{Guid.NewGuid():N}");
        ((CommandHandler)context.Server.CommandHandler).RegisterCommandClass<WorldCommandModule>(null);
        var world = context.World;
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";

        var position = root.GetProperty("player");
        var standing = new VectorD(position[0].GetDouble(), position[1].GetDouble(), position[2].GetDouble());
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Probe", standing);
        player.GameMode = GameMode.Creative;
        Assert.True(world.TryAddPlayer(player));
        await LoadChunksAsync(world, player);
        Assert.True(await context.Server.PauseAsync());

        var client = (TestClient)player.Client;
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        await FixtureCases.AssertAllAsync(cases, scenario => scenario.GetProperty("name").GetString()!,
            scenario => ReplayAsync(context, player, client, scenario));
    }

    private static async Task ReplayAsync(
        RealServerContext context, Entities.Player player, TestClient client, JsonElement scenario)
    {
        var world = context.World;
        foreach (var fill in scenario.GetProperty("fills").EnumerateArray())
        {
            var from = Position(fill.GetProperty("from"));
            Fill(world, from, Position(fill.GetProperty("to")), fill.GetProperty("state"));
        }

        if (scenario.TryGetProperty("hand", out var hand))
        {
            var item = ItemsRegistry.Get(hand.GetString()!);
            player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(item, 1));
        }

        // What the setup sent isn't part of the case.
        await context.Server.RunTickTurnAsync();
        client.ClearPackets();

        var differences = new List<string>();
        var segments = scenario.GetProperty("segments").EnumerateArray().ToArray();
        var actions = scenario.GetProperty("actions").EnumerateArray().ToArray();
        var ticks = scenario.GetProperty("ticks").GetInt32();
        for (var tick = 0; tick <= ticks; tick++)
        {
            if (tick > 0)
            {
                await world.DoWorldTickAsync();
                await context.Server.RunTickTurnAsync();
                Compare(differences, segments, tick, "tick", client);
            }

            var tickActions = actions.Where(action => action.GetProperty("tick").GetInt32() == tick).ToArray();
            if (tickActions.Length == 0)
                continue;

            var feedback = new List<string>();
            foreach (var action in tickActions)
                feedback.AddRange(await PerformAsync(context, player, action));

            Compare(differences, segments, tick, "actions", client, feedback);
        }

        for (var tick = 0; tick < DrainTicks; tick++)
        {
            await world.DoWorldTickAsync();
            await context.Server.RunTickTurnAsync();
        }

        client.ClearPackets();
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    // An action, and the first frozen tick after it. Returns what a command said back.
    private static async Task<List<string>> PerformAsync(
        RealServerContext context, Entities.Player player, JsonElement action)
    {
        var world = context.World;
        var feedback = new Feedback();
        if (action.TryGetProperty("setblock", out var setblock))
        {
            var position = Position(setblock.GetProperty("pos"));
            var block = VanillaSulfurBlocks.ParseState(setblock.GetProperty("state").GetString()!);
            world.PrepareObserverCommandPlacement(position, block);
            await world.SetBlockAsync(position, block, doBlockUpdate: true);
        }
        else if (action.TryGetProperty("fillbiome", out var fillbiome))
        {
            var command = new CommandContext("/" + FillBiomeCommand(fillbiome), feedback, null, context.Server)
            {
                Level = world
            };
            await ((CommandHandler)context.Server.CommandHandler).ProcessCommand(command);
        }
        else if (action.TryGetProperty("use", out var use))
        {
            var face = Face(use.GetProperty("face"));
            var packet = BlockUpdates.UseOn(Position(use.GetProperty("pos")), face, Cursor(face),
                use.GetProperty("sequence").GetInt32());
            await packet.HandleAsync(context.Server, player);
        }
        else if (action.TryGetProperty("break", out var breaking))
        {
            var packet = StartBreaking(Position(breaking.GetProperty("pos")), Face(breaking.GetProperty("face")),
                breaking.GetProperty("sequence").GetInt32());
            await packet.HandleAsync(context.Server, player);
        }
        else
        {
            throw new InvalidOperationException($"Unknown action {action}.");
        }

        await context.Server.RunTickTurnAsync();
        return feedback.Messages;
    }

    // The /fillbiome the capture ran, without its slash.
    private static string FillBiomeCommand(JsonElement fillbiome)
    {
        var from = Text(Position(fillbiome.GetProperty("from"))).Replace(',', ' ');
        var to = Text(Position(fillbiome.GetProperty("to"))).Replace(',', ' ');
        var command = $"fillbiome {from} {to} {fillbiome.GetProperty("biome").GetString()}";
        return fillbiome.TryGetProperty("filter", out var filter) ? $"{command} replace {filter.GetString()}" : command;
    }

    private static void Compare(List<string> differences, JsonElement[] segments, int tick, string after,
        TestClient client, List<string>? feedback = null)
    {
        var segment = segments.SingleOrDefault(candidate => candidate.GetProperty("tick").GetInt32() == tick
            && candidate.GetProperty("after").GetString() == after);
        var found = segment.ValueKind != JsonValueKind.Undefined;
        var packets = found
            ? segment.GetProperty("packets").EnumerateArray().Select(DescribeVanilla)
            : [];
        var said = found && segment.TryGetProperty("feedback", out var lines)
            ? lines.EnumerateArray().Select(line => $"feedback {line.GetString()}")
            : [];
        string[] expected = [.. packets, .. said];
        string[] actual =
        [
            .. client.SentPackets.Select(DescribeSent).OfType<string>(),
            .. (feedback ?? []).Select(line => $"feedback {line}")
        ];
        client.ClearPackets();

        if (expected.SequenceEqual(actual))
            return;

        differences.Add($"tick {tick} after {after}:");
        differences.Add("  vanilla:");
        differences.AddRange(expected.Select(packet => $"    {packet}"));
        differences.Add("  obsidian:");
        differences.AddRange(actual.Select(packet => $"    {packet}"));
    }

    private static string DescribeVanilla(JsonElement packet)
    {
        var type = packet.GetProperty("type").GetString();
        return type switch
        {
            "block_update" => $"block_update {Text(Position(packet.GetProperty("pos")))} "
                + packet.GetProperty("state").GetString(),
            "section_blocks_update" => $"section_blocks_update {Text(Position(packet.GetProperty("section")))} "
                + string.Join(" ", packet.GetProperty("blocks").EnumerateArray()
                    .Select(block => $"{Text(Position(block.GetProperty("pos")))}="
                        + block.GetProperty("state").GetString()))
                + $" bytes {packet.GetProperty("bytes").GetString()}",
            "block_event" => $"block_event {Text(Position(packet.GetProperty("pos")))} "
                + $"{packet.GetProperty("block").GetString()} {packet.GetProperty("action").GetInt32()} "
                + packet.GetProperty("parameter").GetInt32(),
            "block_changed_ack" => $"block_changed_ack {packet.GetProperty("sequence").GetInt32()}",
            "chunks_biomes" => "chunks_biomes " + string.Join(" ", packet.GetProperty("chunks").EnumerateArray()
                .Select(chunk => $"{chunk.GetProperty("pos")[0].GetInt32()},{chunk.GetProperty("pos")[1].GetInt32()}="
                    + chunk.GetProperty("bytes").GetString())),
            _ => throw new InvalidOperationException($"Unknown packet type {type}."),
        };
    }

    // The packets the capture records, described as DescribeVanilla describes them; null for the others.
    private static string? DescribeSent(IClientboundPacket packet) => packet switch
    {
        BlockUpdatePacket update => $"block_update {Text(update.Position)} {StateText(update.BlockId)}",
        SectionBlocksUpdatePacket section => $"section_blocks_update "
            + $"{section.SectionX},{section.SectionY},{section.SectionZ} "
            + string.Join(" ", section.Blocks().Select(block => $"{Text(block.Position)}={StateText(block.StateId)}"))
            + $" bytes {Body(section)}",
        BlockEventPacket blockEvent => $"block_event {Text(blockEvent.Position)} "
            + $"{BlocksRegistry.GetDefault(blockEvent.BlockType).UnlocalizedName} {blockEvent.ActionId} "
            + blockEvent.ActionParam,
        BlockChangedAckPacket ack => $"block_changed_ack {ack.SequenceID}",
        ChunksBiomesPacket biomes => "chunks_biomes " + string.Join(" ", biomes.ChunkBiomes
            .Select(chunk => $"{chunk.X},{chunk.Z}={Convert.ToHexStringLower(chunk.Data.Span)}")),
        _ => null,
    };

    private static string Body(IClientboundPacket packet)
    {
        using var buffer = new NetworkBuffer();
        packet.Serialize(buffer);
        return Convert.ToHexStringLower(buffer.AsSpan(0, buffer.Size));
    }

    private static string StateText(int stateId)
    {
        var block = BlocksRegistry.Get(stateId);
        var properties = block.GetProperties().OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}").ToArray();
        return properties.Length == 0
            ? block.UnlocalizedName
            : $"{block.UnlocalizedName}[{string.Join(",", properties)}]";
    }

    // Like /fill strict: the blocks are stored without updates to them or their neighbors.
    private static void Fill(World world, Vector from, Vector to, JsonElement state)
    {
        var block = VanillaSulfurBlocks.ParseState(state.GetString()!);
        for (var x = from.X; x <= to.X; x++)
        {
            for (var y = from.Y; y <= to.Y; y++)
            {
                for (var z = from.Z; z <= to.Z; z++)
                    world.BlockUpdates.SetBlock(new Vector(x, y, z), block,
                        BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
            }
        }
    }

    // The capture's chunks, and those around them, loaded and sent to the player.
    private static async Task LoadChunksAsync(World world, Entities.Player player)
    {
        for (var chunkX = -2; chunkX <= 2; chunkX++)
        {
            for (var chunkZ = -2; chunkZ <= 2; chunkZ++)
            {
                var chunk = WorldBlockEntities.AddEmptyChunk(world, chunkX, chunkZ);
                await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
                SetVoidBiomes(chunk);
                player.LoadedChunks.Add(NumericsHelper.IntsToLong(chunkX, chunkZ));
            }
        }
    }

    // The capture world's biomes, as vanilla's void superflat generates them: each section's container is made with
    // plains, vanilla's default, and then filled with the_void, which leaves both in its palette, plains first.
    private static void SetVoidBiomes(IChunk chunk)
    {
        Assert.True(CodecRegistry.TryGetBiome("minecraft:the_void", out var theVoid));
        foreach (var section in chunk.Sections)
        {
            section.BiomeContainer.Set(0, 0, 0, CodecRegistry.Biomes.Plains);
            for (var index = 0; index < 64; index++)
                section.BiomeContainer.Set(index & 3, index >> 4, index >> 2 & 3, theVoid!);
        }
    }

    /// <summary>What a command says back.</summary>
    private sealed class Feedback : ICommandSender
    {
        public CommandIssuers Issuer => CommandIssuers.Console;

        public IPlayer? Player => null;

        public List<string> Messages { get; } = [];

        public Task SendMessageAsync(ChatMessage message)
        {
            this.Messages.Add(StoredCommand.PlainText(message));
            return Task.CompletedTask;
        }

        public Task SendMessageAsync(ChatMessage message, Guid sender) => this.SendMessageAsync(message);
    }

    private static PlayerActionPacket StartBreaking(Vector position, BlockFace face, int sequence)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarInt((int)PlayerActionStatus.StartedDigging);
        writer.WritePosition(position);
        writer.WriteByte((byte)face);
        writer.WriteVarInt(sequence);

        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new PlayerActionPacket();
        packet.Populate(reader);

        return packet;
    }

    private static BlockFace Face(JsonElement face) => (BlockFace)Array.IndexOf(Faces, face.GetString());

    // The centre of a block's face, relative to the block, as the capture's client clicks.
    private static VectorF Cursor(BlockFace face) => (int)face switch
    {
        0 => new VectorF(0.5f, 0, 0.5f),
        1 => new VectorF(0.5f, 1, 0.5f),
        2 => new VectorF(0.5f, 0.5f, 0),
        3 => new VectorF(0.5f, 0.5f, 1),
        4 => new VectorF(0, 0.5f, 0.5f),
        _ => new VectorF(1, 0.5f, 0.5f),
    };

    private static Vector Position(JsonElement array) =>
        new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    private static string Text(Vector position) => $"{position.X},{position.Y},{position.Z}";

    private static JsonDocument LoadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FixtureResource)!;
        return JsonDocument.Parse(stream);
    }
}
