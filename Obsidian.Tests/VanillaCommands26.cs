using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Commands;
using Obsidian.API.Configuration;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Utilities;
using Obsidian.API.Registries;
using Obsidian.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Modules;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class VanillaCommands26
{
    // The player inventory index of hotbar.0.
    private const int FirstHotbarSlot = 36;

    private static readonly JsonElement fixture = ReadFixture();

    private static JsonElement ReadFixture()
    {
        using var stream = typeof(Server).Assembly.GetManifestResourceStream("Obsidian.Assets.commands-26.3.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    public static IEnumerable<object[]> ComputeCases => Rows("compute");
    public static IEnumerable<object[]> TimeCases => Rows("time");
    public static IEnumerable<object[]> SlotCases => Rows("slots");
    public static IEnumerable<object[]> DataCases => Rows("data");
    public static IEnumerable<object[]> SpamCases => Rows("spam");
    public static IEnumerable<object[]> ItemCountCases => Rows("item_counts");
    public static IEnumerable<object[]> NumericTagCases => Rows("numeric_tags");

    private static IEnumerable<object[]> Rows(string section) => fixture.GetProperty(section).EnumerateArray()
        .Select((row, index) => new object[] { $"{section}-{index}", row.GetRawText() });

    [Theory]
    [MemberData(nameof(ComputeCases))]
    public async Task ComputeResultsMatchVanilla(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        await using var server = TestServerFactory.CreateServer($"{name}-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<ComputeCommandModule>(null);
        var context = Context(server.Server, row.GetProperty("command").GetString()!);

        await handler.ProcessCommand(context);

        Assert.Equal(row.GetProperty("success").GetBoolean(), context.Succeeded);
        Assert.Equal(row.GetProperty("result").GetInt32(), context.Result);
    }

    [Theory]
    [InlineData("this", "player", 1)]
    [InlineData("this", "zombie", 0)]
    [InlineData("target_entity", "zombie", 1)]
    [InlineData("target_entity", "player", 0)]
    public async Task ComputeEntityContextsKeepTheExecutorAndTheTargetApart(string target, string type, int expected)
    {
        await using var server = TestServerFactory.CreateServer($"compute-entity-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<ComputeCommandModule>(null);
        WorldBlockEntities.AddEmptyChunk(server.World);
        var player = AddFixturePlayer(server);
        var zombie = server.World.SpawnEntity(new VectorD(0.5, 64, 0.5), EntityType.Zombie);
        var condition = $"{{type:'entity_properties',entity:'{target}',predicate:{{entity_type:'{type}'}}}}";
        var provider = $"{{type:'conditional',condition:{condition},on_true:1,on_false:0}}";
        var command = $"/compute entity {zombie.Uuid} integer {provider}";
        var context = new CommandContext(command, new CommandSender(CommandIssuers.Console, null), player,
            server.Server);

        await handler.ProcessCommand(context);

        Assert.True(context.Succeeded);
        Assert.Equal(expected, context.Result);
    }

    [Theory]
    [MemberData(nameof(TimeCases))]
    public async Task ClockResultsAndFailuresMatchVanilla(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        await using var server = TestServerFactory.CreateServer($"{name}-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<TimeCommandModule>(null);
        var clock = server.World.LevelData.WorldClocks.States[WorldClocks.Overworld];
        clock.TotalTicks = 48001;
        clock.PartialTick = 0.75f;
        clock.Rate = 1;
        clock.Paused = row.GetProperty("paused").GetBoolean();
        var context = Context(server.Server, row.GetProperty("command").GetString()!);

        await handler.ProcessCommand(context);

        Assert.Equal(row.GetProperty("success").GetBoolean(), context.Succeeded);
        Assert.Equal(row.GetProperty("result").GetInt32(), context.Result);
        Assert.Equal(row.GetProperty("ticks").GetInt64(), clock.TotalTicks);
        Assert.Equal(row.GetProperty("partial").GetSingle(), clock.PartialTick);
        Assert.Equal(row.GetProperty("rate").GetSingle(), clock.Rate);
        Assert.Equal(row.GetProperty("afterPaused").GetBoolean(), clock.Paused);
    }

    [Theory]
    [MemberData(nameof(SlotCases))]
    public async Task SlotSourcesAndDistributionMatchVanilla(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        await using var server = TestServerFactory.CreateServer($"{name}-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<ItemCommandModule>(null);
        handler.RegisterCommandClass<ExecuteCommandModule>(null);
        var position = new Vector(0, 64, 0);
        WorldBlockEntities.AddEmptyChunk(server.World);
        var inventories = new List<FourSlots>();
        for (var target = 0; target < 2; target++)
        {
            var inventory = new FourSlots();
            for (var slot = 0; slot < inventory.Size; slot++)
                inventory.SetItem(slot, new ItemStack(ItemsRegistry.OakPlanks, 3));

            if (Holder(row.GetProperty("holder").GetString()!) is { } holder)
                inventory.SetItem(0, holder);

            var at = position + new Vector(target, 0, 0);
            await server.World.SetBlockEntity(at, new ContainerBlockEntity(inventory)
            {
                Id = "minecraft:chest",
                BlockPosition = at
            });
            inventories.Add(inventory);
        }

        var expression = row.GetProperty("expression").GetString()!;
        var operation = row.GetProperty("operation").GetString()!;
        var selected = await CommandSlots.ReadAsync(Context(server.Server, "item"),
            new CommandInput($"block 0 64 0 {expression}"));
        var other = await CommandSlots.ReadAsync(Context(server.Server, "item"),
            new CommandInput($"block 1 64 0 {expression}"));
        var items = new List<ItemStack>
        {
            new(ItemsRegistry.Stone, 2),
            new(ItemsRegistry.Dirt, 1)
        };

        var changed = await ItemCommandModule.DistributeAsync([.. selected, .. other], items, operation);
        var condition = Context(server.Server, $"execute if slots block 0 64 0 {expression}");
        await handler.ProcessCommand(condition);

        Assert.Equal(row.GetProperty("slots").GetInt32(), CommandSlots.CountVisible(selected));
        Assert.Equal(row.GetProperty("changed").GetInt32(), changed);
        var afterSlots = row.GetProperty("afterSlots").GetInt32();
        Assert.Equal(afterSlots > 0, condition.Succeeded);
        Assert.Equal(afterSlots, condition.Result);
        var expected = row.GetProperty("after").EnumerateArray().ToArray();
        for (var index = 0; index < expected.Length; index++)
        {
            var inventory = inventories[index / 4];
            AssertStack(expected[index], inventory.GetItem(index % 4) ?? ItemStack.Air);
        }
    }

    [Theory]
    [MemberData(nameof(ItemCountCases))]
    public async Task CreatedItemStacksAreValidatedLikeVanilla(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        await using var server = TestServerFactory.CreateServer($"{name}-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<ItemCommandModule>(null);
        var player = AddFixturePlayer(server);
        var item = row.GetProperty("item").GetString();
        var count = row.GetProperty("count").GetInt32();
        var context = Context(server.Server, $"item replace entity Fixture hotbar.0 with {item} {count}");

        await handler.ProcessCommand(context);

        var success = row.GetProperty("success").GetBoolean();
        Assert.Equal(success, context.Succeeded);
        var stack = player.Inventory.GetItem(FirstHotbarSlot);
        var stored = stack is { IsAir: false } ? stack.Count : 0;
        Assert.Equal(success ? count : 0, stored);
    }

    [Fact]
    public async Task ClockMarkerCompletionsMatchVanilla()
    {
        await using var server = TestServerFactory.CreateServer($"command-completion-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<TimeCommandModule>(null);

        foreach (var row in fixture.GetProperty("marker_completions").EnumerateArray())
        {
            var clock = row.GetProperty("clock").GetString();
            var command = $"time of {clock} set ";
            var context = Context(server.Server, command);

            var completion = await handler.CompleteAsync(context);

            Assert.Equal(command.Length + 1, completion.Start);
            Assert.Equal(0, completion.Length);
            Assert.Equal(row.GetProperty("suggestions").EnumerateArray().Select(value => value.GetString()),
                completion.Suggestions.Select(value => value.Text));
        }
    }

    [Fact]
    public async Task ItemCommandSnapshotsOverlappingSources()
    {
        var reference = fixture.GetProperty("slots").EnumerateArray().First(row =>
            row.GetProperty("expression").GetString()!.StartsWith('[')
            && row.GetProperty("operation").GetString() == "replace");
        var expression = reference.GetProperty("expression").GetString();
        await using var server = TestServerFactory.CreateServer($"item-overlap-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<ItemCommandModule>(null);
        WorldBlockEntities.AddEmptyChunk(server.World);
        var inventory = new FourSlots();
        inventory.SetItem(0, new ItemStack(ItemsRegistry.Stone, 2));
        inventory.SetItem(1, new ItemStack(ItemsRegistry.Dirt, 1));
        inventory.SetItem(2, new ItemStack(ItemsRegistry.OakPlanks, 3));
        inventory.SetItem(3, new ItemStack(ItemsRegistry.OakPlanks, 3));
        var position = new Vector(0, 64, 0);
        await server.World.SetBlockEntity(position, new ContainerBlockEntity(inventory)
        {
            Id = "minecraft:chest",
            BlockPosition = position
        });
        const string source = "{type:'limit_slots',limit:2,slot_source:{type:'slot_range',slots:'container.*'}}";
        var context = Context(server.Server, $"item replace block 0 64 0 {expression} from block 0 64 0 {source}");

        await handler.ProcessCommand(context);

        Assert.True(context.Succeeded);
        var expected = reference.GetProperty("after").EnumerateArray().Take(inventory.Size).ToArray();
        for (var slot = 0; slot < inventory.Size; slot++)
            AssertStack(expected[slot], inventory.GetItem(slot)!);
    }

    [Theory]
    [MemberData(nameof(DataCases))]
    public void ComputedNbtMutationsMatchVanilla(string name, string json)
    {
        Assert.StartsWith("data-", name);
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var values = new NbtList(NbtTagType.Int, "values")
        {
            new NbtTag<int>("", 1),
            new NbtTag<int>("", 2)
        };
        var root = new NbtCompound
        {
            new NbtTag<int>("value", 7),
            values
        };
        var succeeded = false;

        try
        {
            succeeded = DataCommandModule.ModifyNumber(root, row.GetProperty("path").GetString()!,
                row.GetProperty("operation").GetString()!, 0, new NbtTag<int>("", 7));
        }
        catch (FormatException)
        {
            // Vanilla's captured failure is checked together with the unchanged data below.
        }

        Assert.Equal(row.GetProperty("success").GetBoolean(), succeeded);
        AssertJsonNbt(row.GetProperty("after"), root);
    }

    [Theory]
    [MemberData(nameof(SpamCases))]
    public void SeparateSpamBudgetsMatchVanilla(string name, string json)
    {
        Assert.StartsWith("spam-", name);
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var threshold = row.GetProperty("threshold").GetInt32();
        var budget = new CommandSpam.Budget();

        foreach (var action in row.GetProperty("events").EnumerateArray())
        {
            switch (action.GetProperty("operation").GetString())
            {
                case "tick":
                    budget.Tick();
                    break;
                case "chat":
                    budget.Record(false, threshold);
                    break;
                case "command":
                    budget.Record(true, threshold);
                    break;
            }

            Assert.Equal(action.GetProperty("chat").GetBoolean(), budget.UnderThreshold(false, threshold));
            Assert.Equal(action.GetProperty("command").GetBoolean(), budget.UnderThreshold(true, threshold));
        }
    }

    [Theory]
    [MemberData(nameof(NumericTagCases))]
    public void NumericNbtConvertsLikeVanilla(string name, string json)
    {
        Assert.StartsWith("numeric_tags-", name);
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var tag = NumericTag(row.GetProperty("type").GetString()!, row.GetProperty("value").GetString()!);

        Assert.True(tag.TryGetNumber(out byte asByte));
        Assert.True(tag.TryGetNumber(out short asShort));
        Assert.True(tag.TryGetNumber(out int asInt));
        Assert.True(tag.TryGetNumber(out long asLong));
        Assert.True(tag.TryGetNumber(out float asFloat));
        Assert.True(tag.TryGetNumber(out double asDouble));

        Assert.Equal(row.GetProperty("byte").GetInt32(), unchecked((sbyte)asByte));
        Assert.Equal(row.GetProperty("short").GetInt16(), asShort);
        Assert.Equal(row.GetProperty("int").GetInt32(), asInt);
        Assert.Equal(row.GetProperty("long").GetInt64(), asLong);
        Assert.Equal(float.Parse(row.GetProperty("float").GetString()!, CultureInfo.InvariantCulture), asFloat);
        Assert.Equal(double.Parse(row.GetProperty("double").GetString()!, CultureInfo.InvariantCulture), asDouble);
    }

    [Fact]
    public async Task PostEffectResultsAndPacketsMatchVanilla()
    {
        await using var server = TestServerFactory.CreateServer($"posteffect-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<PostEffectCommandModule>(null);
        var player = TestServerFactory.CreateConnectedPlayer(server.Server, server.World, "Fixture");
        server.Server.AddPlayer(player);
        var client = (TestClient)player.Client;

        foreach (var row in fixture.GetProperty("posteffect").EnumerateArray())
        {
            client.ClearPackets();
            var operation = row.GetProperty("operation").GetString();
            var suffix = operation is "add" or "remove" ? " test:effect" : string.Empty;
            var context = Context(server.Server, $"posteffect {operation} Fixture{suffix}");

            await handler.ProcessCommand(context);

            Assert.Equal(row.GetProperty("success").GetBoolean(), context.Succeeded);
            Assert.Equal(row.GetProperty("result").GetInt32(), context.Result);
            var packets = client.SentPackets.OfType<PostEffectsPacket>().ToArray();
            if (context.Succeeded && operation != "list")
            {
                var packet = Assert.Single(packets);
                Assert.Equal(row.GetProperty("effects").EnumerateArray().Select(effect => effect.GetString()),
                    packet.PostEffects);
            }
            else
            {
                Assert.Empty(packets);
            }
        }
    }

    [Fact]
    public void DefaultsMatchVanilla()
    {
        var expected = fixture.GetProperty("defaults");
        var configuration = ServerConfiguration.CreateDefault();

        Assert.Equal(expected.GetProperty("whitelist").GetBoolean(), configuration.Whitelist);
        Assert.Equal(expected.GetProperty("chatSpamThresholdSeconds").GetInt32(), configuration.ChatSpamThresholdSeconds);
        Assert.Equal(expected.GetProperty("commandSpamThresholdSeconds").GetInt32(),
            configuration.CommandSpamThresholdSeconds);
    }

    [Fact]
    public async Task CommandTreeParsersSuggestionsAndRedirectsMatchVanilla()
    {
        await using var server = TestServerFactory.CreateServer($"command-tree-{Guid.NewGuid():N}");
        ((CommandHandler)server.Server.CommandHandler).RegisterCommands();
        ((CommandHandler)server.Server.CommandHandler).RegisterCommandClass<IntegratedCommandModule>(null);

        CommandsRegistry.Register(server.Server);

        var packet = CommandsRegistry.Packet;
        var root = packet.Nodes[0];
        foreach (var expected in fixture.GetProperty("tree").EnumerateObject())
        {
            var actual = Assert.Single(root.Children, node => node.Name == expected.Name);
            AssertNode(expected.Value, actual, packet.Nodes);
        }

        using var wire = new NetworkBuffer();
        packet.Serialize(wire);
        using var reader = new NetworkBuffer(wire.AsSpan(0, wire.Size).ToArray());
        Assert.Equal(packet.Nodes.Count, reader.ReadVarInt());
        foreach (var node in packet.Nodes)
        {
            Assert.Equal((byte)node.Type, reader.ReadByte());
            Assert.Equal(node.Children.Count, reader.ReadVarInt());
            foreach (var child in node.Children)
                Assert.Equal(child.Index, reader.ReadVarInt());

            if (node.Type.HasFlag(CommandNodeType.HasRedirect))
                Assert.Equal(node.RedirectIndex, reader.ReadVarInt());

            if (node.Name is not null)
                Assert.Equal(node.Name, reader.ReadString());

            if (node.Parser is not null)
            {
                using var parser = new NetworkBuffer();
                node.Parser.Write(parser);
                for (var index = 0; index < parser.Size; index++)
                    Assert.Equal(parser[index], unchecked((byte)reader.ReadByte()));
            }

            if (node.SuggestionType is not null)
                Assert.Equal(node.SuggestionType, reader.ReadString());
        }

        Assert.Equal(0, reader.ReadVarInt());
        Assert.Equal(reader.Size, reader.Offset);
    }

    [Fact]
    public async Task ExecutePreservesFailuresAndSuccessfulZeroResults()
    {
        await using var server = TestServerFactory.CreateServer($"command-results-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<ExecuteCommandModule>(null);
        handler.RegisterCommandClass<ComputeCommandModule>(null);
        var zero = Context(server.Server, "execute run compute default integer {type:'minecraft:constant',value:0}");
        var failure = Context(server.Server,
            "execute run compute default integer {type:'minecraft:div',left:1,right:0}");

        await handler.ProcessCommand(zero);
        await handler.ProcessCommand(failure);

        Assert.True(zero.Succeeded);
        Assert.Equal(0, zero.Result);
        Assert.False(failure.Succeeded);
        Assert.Equal(0, failure.Result);
    }

    [Fact]
    public async Task ComputedDataCommitsToStorageAndFeedsOtherProviders()
    {
        await using var server = TestServerFactory.CreateServer($"command-storage-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<DataCommandModule>(null);
        handler.RegisterCommandClass<ComputeCommandModule>(null);
        var captured = fixture.GetProperty("compute")[1];
        var expression = captured.GetProperty("command").GetString()!.Split("integer ", 2)[1];
        var command = $"data modify storage test:values result set compute default integer {expression}";
        var write = Context(server.Server, command);
        var read = Context(server.Server,
            "compute default integer {type:'minecraft:storage',storage:'test:values',path:'result'}");

        await handler.ProcessCommand(write);
        await handler.ProcessCommand(read);

        Assert.True(write.Succeeded);
        Assert.True(read.Succeeded);
        Assert.Equal(captured.GetProperty("result").GetInt32(), read.Result);
        Assert.Equal(read.Result, CommandStorage.Read(server.Server, "test:values").GetInt("result"));
        Assert.True(File.Exists(Path.Combine(server.World.WorldFolderPath, "data", "test", "command_storage.dat")));
    }

    [Theory]
    [InlineData("integer", "5", 5f)]
    [InlineData("float", "7.5", 7.5f)]
    public async Task ComputedEntityDataReachesTheLiveEntity(string kind, string value, float health)
    {
        await using var server = TestServerFactory.CreateServer($"command-entity-data-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<DataCommandModule>(null);
        WorldBlockEntities.AddEmptyChunk(server.World);
        var zombie = (Obsidian.Entities.Living)server.World.SpawnEntity(new VectorD(0.5, 64, 0.5), EntityType.Zombie);
        var provider = $"{{type:'constant',value:{value}}}";
        var command = $"data modify entity {zombie.Uuid} Health set compute default {kind} {provider}";
        var context = Context(server.Server, command);

        await handler.ProcessCommand(context);

        Assert.True(context.Succeeded);
        Assert.Equal(health, zombie.Health);
    }

    [Fact]
    public async Task InlineFeaturesUseTheCurrentFeatureMachinery()
    {
        await using var server = TestServerFactory.CreateServer($"command-feature-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<PlaceCommandModule>(null);
        for (var x = -1; x <= 1; x++)
        {
            for (var z = -1; z <= 1; z++)
                WorldBlockEntities.AddEmptyChunk(server.World, x, z);
        }

        var command = Context(server.Server, "place feature {type:'no_op'} 0 64 0");

        await handler.ProcessCommand(command);

        Assert.Equal(fixture.GetProperty("no_op_feature").GetBoolean(), command.Succeeded);
        Assert.Equal(1, command.Result);
    }

    [Theory]
    [InlineData("swing", InteractionHand.MainHand)]
    [InlineData("swing @s mainhand", InteractionHand.MainHand)]
    [InlineData("swing @s offhand", InteractionHand.OffHand)]
    public async Task SwingIncludesTheExecutingPlayer(string command, InteractionHand hand)
    {
        await using var server = TestServerFactory.CreateServer($"command-swing-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<SwingCommandModule>(null);
        var player = TestServerFactory.CreateConnectedPlayer(server.Server, server.World, "Fixture");
        WorldBlockEntities.AddEmptyChunk(server.World);
        server.Server.AddPlayer(player);
        server.World.Players.TryAdd(player.Uuid, player);
        var context = new CommandContext("/" + command,
            new CommandSender(CommandIssuers.Console, null), player, server.Server);
        var client = (TestClient)player.Client;
        client.ClearPackets();

        await handler.ProcessCommand(context);

        Assert.True(context.Succeeded);
        Assert.Equal(1, context.Result);
        var packet = Assert.Single(client.SentPackets.OfType<SwingAnimationPacket>());
        Assert.Equal(hand, packet.Hand);
        Assert.Equal(fixture.GetProperty("swing_default").GetProperty("duration").GetInt32(), packet.Animation.Duration);
        Assert.Equal(fixture.GetProperty("swing_default").GetProperty("type").GetString(),
            packet.Animation.Type.ToString().ToLowerInvariant());
    }

    [Fact]
    public async Task CustomCommandsKeepTheirGrammarAndAwaitIntegerResults()
    {
        await using var server = TestServerFactory.CreateServer($"custom-compute-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommandClass<CustomComputeModule>(null);
        var context = Context(server.Server, "compute");

        await handler.ProcessCommand(context);
        CommandsRegistry.Register(server.Server);

        Assert.True(context.Succeeded);
        Assert.Equal(37, context.Result);
        var command = Assert.Single(CommandsRegistry.Packet.Nodes[0].Children);
        Assert.Empty(command.Children);
        Assert.True(command.Type.HasFlag(CommandNodeType.IsExecutable));
    }

    [Theory]
    [InlineData("compute default integer {type:'constant',value:1}")]
    [InlineData("time pause")]
    [InlineData("swing")]
    [InlineData("posteffect clear @s")]
    [InlineData("item fill entity @s hotbar.* with stone")]
    [InlineData("place feature {type:'no_op'}")]
    [InlineData("data modify storage test:values x set compute default integer {type:'constant',value:1}")]
    public async Task GameMasterCommandsRequireTheSendersPermissions(string command)
    {
        await using var server = TestServerFactory.CreateServer($"command-permission-{Guid.NewGuid():N}");
        var handler = (CommandHandler)server.Server.CommandHandler;
        handler.RegisterCommands();
        var player = TestServerFactory.CreateConnectedPlayer(server.Server, server.World, "Guest");
        var context = new CommandContext("/" + command,
            new CommandSender(CommandIssuers.Client, player), player, server.Server);

        await handler.ProcessCommand(context);

        Assert.False(context.Succeeded);
        Assert.Equal(0, context.Result);
    }

    public sealed class CustomComputeModule : CommandModuleBase
    {
        [Command("compute")]
        public async Task<int> ComputeAsync()
        {
            await Task.Yield();
            return 37;
        }
    }

    private static Obsidian.Entities.Player AddFixturePlayer(RealServerContext server)
    {
        var player = TestServerFactory.CreateConnectedPlayer(server.Server, server.World, "Fixture");
        server.Server.AddPlayer(player);
        return player;
    }

    // Builds the tag a captured SNBT number (like -2.5f or 300s) stands for.
    private static INbtTag NumericTag(string type, string snbt)
    {
        var text = snbt == "NaNd" ? "NaN" : snbt.TrimEnd('b', 's', 'L', 'f', 'd');

        return type switch
        {
            "BYTE" => new NbtTag<byte>("", unchecked((byte)sbyte.Parse(text, CultureInfo.InvariantCulture))),
            "SHORT" => new NbtTag<short>("", short.Parse(text, CultureInfo.InvariantCulture)),
            "INT" => new NbtTag<int>("", int.Parse(text, CultureInfo.InvariantCulture)),
            "LONG" => new NbtTag<long>("", long.Parse(text, CultureInfo.InvariantCulture)),
            "FLOAT" => new NbtTag<float>("", float.Parse(text, CultureInfo.InvariantCulture)),
            _ => new NbtTag<double>("", double.Parse(text, CultureInfo.InvariantCulture))
        };
    }

    private static CommandContext Context(Server server, string command) =>
        new("/" + command, new CommandSender(CommandIssuers.Console, null), null, server);

    private sealed class FourSlots() : BaseContainer(4);

    private static void AssertNode(JsonElement expected, CommandNode actual, List<CommandNode> nodes)
    {
        Assert.Same(actual, nodes[actual.Index]);
        var executable = expected.TryGetProperty("executable", out var flag) && flag.GetBoolean();
        Assert.Equal(executable, actual.Type.HasFlag(CommandNodeType.IsExecutable));
        Assert.Equal(expected.GetProperty("restricted").GetBoolean(), actual.Type.HasFlag(CommandNodeType.IsRestricted));
        var suggestion = expected.TryGetProperty("suggestions", out var provider) ? provider.GetString() : null;
        Assert.Equal(suggestion, actual.SuggestionType);
        Assert.Equal(suggestion is not null, actual.Type.HasFlag(CommandNodeType.HasSuggestions));
        if (expected.TryGetProperty("parser", out var parser))
        {
            Assert.NotNull(actual.Parser);
            Assert.Equal(parser.GetString(), actual.Parser.Identifier);
            using var buffer = new NetworkBuffer();
            actual.Parser.Write(buffer);
            Assert.Equal(Convert.FromHexString(expected.GetProperty("wire").GetString()!),
                buffer.AsSpan(0, buffer.Size).ToArray());
        }

        if (expected.TryGetProperty("redirect", out var redirect))
        {
            var target = nodes[0];
            foreach (var part in redirect.EnumerateArray())
                target = Assert.Single(target.Children, child => child.Name == part.GetString());

            Assert.True(actual.Type.HasFlag(CommandNodeType.HasRedirect));
            Assert.Equal(target.Index, actual.RedirectIndex);
        }

        var children = expected.TryGetProperty("children", out var entries)
            ? entries.EnumerateObject().ToArray()
            : [];
        Assert.Equal(children.Length, actual.Children.Count);
        foreach (var child in children)
        {
            var found = Assert.Single(actual.Children, node => node.Name == child.Name);
            AssertNode(child.Value, found, nodes);
        }
    }

    private static void AssertJsonNbt(JsonElement expected, INbtTag actual)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var compound = Assert.IsType<NbtCompound>(actual);
                Assert.Equal(expected.EnumerateObject().Count(), compound.Count);
                foreach (var entry in expected.EnumerateObject())
                    AssertJsonNbt(entry.Value, compound[entry.Name]);

                break;

            case JsonValueKind.Array:
                var list = Assert.IsType<NbtList>(actual);
                var entries = expected.EnumerateArray().ToArray();
                Assert.Equal(entries.Length, list.Count);
                for (var index = 0; index < entries.Length; index++)
                    AssertJsonNbt(entries[index], list[index]);

                break;

            default:
                Assert.Equal(expected.GetInt32(), Assert.IsType<NbtTag<int>>(actual).Value);
                break;
        }
    }

    // The captured slot 0 item whose contents an expression reaches into.
    private static ItemStack? Holder(string holder)
    {
        switch (holder)
        {
            case "chest":
                return new ItemStack(ItemsRegistry.Chest, 1, ComponentBuilder.Container with
                {
                    Value = [new ItemStack(ItemsRegistry.OakPlanks, 3), new ItemStack(ItemsRegistry.OakPlanks, 4)]
                });

            case "bundle":
                return new ItemStack(ItemsRegistry.Bundle, 1);

            case "dirt_bundle":
                var contents = ComponentBuilder.BundleContents with { Value = [new ItemStack(ItemsRegistry.Dirt, 60)] };
                return new ItemStack(ItemsRegistry.Bundle, 1, contents);

            case "crossbow":
                return new ItemStack(ItemsRegistry.Crossbow, 1);

            default:
                return null;
        }
    }

    private static void AssertStack(JsonElement expected, ItemStack stack)
    {
        Assert.Equal(expected.GetProperty("item").GetString(), stack.Holder.UnlocalizedName);
        Assert.Equal(expected.GetProperty("count").GetInt32(), stack.Count);
        if (!expected.TryGetProperty("contents", out var contents))
            return;

        var type = expected.GetProperty("component").GetString() switch
        {
            "minecraft:bundle_contents" => DataComponentType.BundleContents,
            "minecraft:charged_projectiles" => DataComponentType.ChargedProjectiles,
            _ => DataComponentType.Container
        };
        var component = stack.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(type);
        var actual = component?.Value ?? [];
        var items = contents.EnumerateArray().ToArray();
        Assert.Equal(items.Length, actual.Length);
        for (var index = 0; index < items.Length; index++)
            AssertStack(items[index], actual[index]);
    }
}
