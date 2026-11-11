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
using Obsidian.Entities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>Replays structure editor packets and redstone against real 26.3 captures, without pinned outputs.</summary>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaStructureBlocks(ITestOutputHelper output)
{
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace
        | BlockUpdateFlags.SkipBlockEntitySideEffects | BlockUpdateFlags.SuppressDrops;

    private sealed record Harness(RealServerContext Context, Vector Origin, ServerPlayer Operator);

    [Fact]
    public async Task ReplayStructureBlockReference()
    {
        using var stream = typeof(VanillaStructureBlocks).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.structure_blocks.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());
        var harness = await StartAsync(Position(root.GetProperty("origin")));
        await using var context = harness.Context;
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();

        await FixtureCases.AssertAllAsync(cases, test => test.GetProperty("name").GetString()!, async test =>
        {
            try
            {
                await ReplayAsync(harness, test);
            }
            catch (Exception error)
            {
                output.WriteLine(error.ToString());
                throw;
            }
        });
        output.WriteLine($"Replayed {cases.Length} structure block cases.");
    }

    private static async Task<Harness> StartAsync(Vector origin)
    {
        var context = TestServerFactory.CreateServer($"structure-blocks-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";
        for (var x = -6; x <= 6; x++)
        {
            for (var z = -6; z <= 6; z++)
                WorldBlockEntities.AddEmptyChunk(world, x, z);
        }

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Operator",
            (VectorD)origin + new VectorD(.5, 0, -8.5));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        MakeOperator(context.Server, player);
        ((CommandHandler)context.Server.CommandHandler)
            .RegisterCommandClass<Obsidian.Commands.Modules.PlaceCommandModule>(null);
        player.GameMode = GameMode.Creative;
        return new Harness(context, origin, player);
    }

    private static async Task ReplayAsync(Harness harness, JsonElement test)
    {
        var world = harness.Context.World;
        foreach (var entity in world.SnapshotEntities().Where(entity => entity is not IPlayer))
            await world.DestroyEntityAsync(entity);

        foreach (var position in Area(harness))
            world.BlockUpdates.SetBlock(position, BlocksRegistry.Air, Quiet);

        // Retire the previous case's block entities before removing its drops and installing the next case.
        await world.RunQueuedEntityActionsAsync();
        foreach (var entity in world.SnapshotEntities().Where(entity => entity is not IPlayer).ToArray())
            await world.DestroyEntityAsync(entity);
        var templates = StructureBlockTemplates.For(world);
        foreach (var name in new[] { "test:roundtrip", "test:redstone", "test:empty", "test:strict" })
            templates.Remove(name);

        if (Directory.Exists(templates.Folder))
        {
            foreach (var file in Directory.EnumerateFiles(templates.Folder, "*", SearchOption.AllDirectories))
                File.Delete(file);
        }

        foreach (var placement in test.GetProperty("setup").EnumerateArray())
        {
            var position = harness.Origin + Position(placement.GetProperty("offset"));
            var state = VanillaSulfurBlocks.ParseState(placement.GetProperty("state").GetString()!);
            world.BlockUpdates.SetBlock(position, state, Quiet);
            if (placement.TryGetProperty("nbt", out var nbt))
            {
                var entity = (Obsidian.API.BlockEntities.BlockEntity)world.GetResidentBlockEntity(position)!;
                entity.Load(SnbtReader.ReadCompound(nbt.GetString()!));
            }

            if (placement.TryGetProperty("author", out _))
                StructureBlocks.OnPlacedByPlayer(world, position, harness.Operator);
        }

        if (test.TryGetProperty("entitySetup", out var entities))
        {
            foreach (var nbt in entities.EnumerateArray())
            {
                var entity = EntityNbt.Load(SnbtReader.ReadCompound(nbt.GetString()!), world)!;
                Assert.True(world.TrySpawnEntity(entity, hostOwned: true));
            }
        }

        var differences = new List<string>();
        var index = 0;
        foreach (var action in test.GetProperty("actions").EnumerateArray())
        {
            ((TestClient)harness.Operator.Client).ClearPackets();
            if (action.TryGetProperty("edit", out var edit))
            {
                harness.Operator.GameMode = action.TryGetProperty("survival", out _)
                    ? GameMode.Survival : GameMode.Creative;
                SetPermission(harness, action.TryGetProperty("nonOp", out _) ? 0 : 4);
                await Packet(harness.Origin, edit).HandleAsync(harness.Context.Server, harness.Operator);
                SetPermission(harness, 4);
            }
            else if (action.TryGetProperty("validatePath", out _))
            {
                // Compared below against the real 26.3 TemplatePathFactory, without opening a native path.
            }
            else if (action.TryGetProperty("command", out var command))
            {
                var text = "/" + command.GetString();
                var sender = new Obsidian.API.Commands.CommandSender(CommandIssuers.Client, harness.Operator);
                var commandContext = new CommandContext(text, sender, harness.Operator, harness.Context.Server);
                await ((CommandHandler)harness.Context.Server.CommandHandler).ProcessCommand(commandContext);
            }
            else
            {
                var position = harness.Origin + Position(action.GetProperty("offset"));
                var state = VanillaSulfurBlocks.ParseState(action.GetProperty("state").GetString()!);
                var flags = action.TryGetProperty("quiet", out _) ? Quiet : BlockUpdateFlags.All;
                world.BlockUpdates.SetBlock(position, state, flags);
            }

            await world.RunQueuedEntityActionsAsync();
            var actual = Record(harness);
            if (action.TryGetProperty("validatePath", out var path))
                actual["pathValid"] = Obsidian.WorldData.Structures.StructureTemplateNames.IsValid(path.GetString()!);
            var expected = JsonNode.Parse(test.GetProperty("checkpoints")[index].GetRawText());
            CheckKnownGaps(test, index, expected!, actual, differences);
            Compare(expected, actual, $"action {index + 1}", differences);
            index++;
        }

        foreach (var placement in test.GetProperty("setup").EnumerateArray())
        {
            var position = harness.Origin + Position(placement.GetProperty("offset"));
            world.BlockUpdates.SetBlock(position, BlocksRegistry.Air, Quiet);
        }

        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    private static void SetPermission(Harness harness, int level)
    {
        var field = typeof(OperatorList).GetField("operators", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var entries = (IList)field.GetValue(harness.Context.Server.Operators)!;
        var entry = entries[0]!;
        entry.GetType().GetProperty("Level")!.SetValue(entry, level);
        entries[0] = entry;
    }

    internal static SetStructureBlockPacket Packet(Vector origin, JsonElement edit)
    {
        using var buffer = new NetworkBuffer();
        buffer.WritePosition(origin + Position(edit.GetProperty("offset")));
        buffer.WriteVarInt(edit.GetProperty("action").GetInt32());
        buffer.WriteVarInt(edit.GetProperty("mode").GetInt32());
        buffer.WriteString(edit.GetProperty("name").GetString()!);
        foreach (var coordinate in edit.GetProperty("pos").EnumerateArray())
            buffer.WriteByte(unchecked((byte)coordinate.GetInt32()));

        foreach (var coordinate in edit.GetProperty("size").EnumerateArray())
            buffer.WriteByte(unchecked((byte)coordinate.GetInt32()));

        buffer.WriteVarInt(edit.GetProperty("mirror").GetInt32());
        buffer.WriteVarInt(edit.GetProperty("rotation").GetInt32());
        buffer.WriteString(edit.GetProperty("metadata").GetString()!);
        buffer.WriteSingle(edit.GetProperty("integrity").GetSingle());
        buffer.WriteVarLong(edit.GetProperty("seed").GetInt64());
        var flags = (edit.GetProperty("ignoreEntities").GetBoolean() ? 1 : 0)
            | (edit.GetProperty("showAir").GetBoolean() ? 2 : 0)
            | (edit.GetProperty("showBox").GetBoolean() ? 4 : 0)
            | (edit.GetProperty("strict").GetBoolean() ? 8 : 0);
        buffer.WriteByte((byte)flags);
        return SetStructureBlockPacket.Deserialize(buffer.AsSpan(0, buffer.Size).ToArray());
    }

    private static JsonObject Record(Harness harness)
    {
        var world = harness.Context.World;
        var blocks = new JsonArray();
        var saved = new JsonObject();
        var blockEntities = new JsonObject();
        foreach (var position in Area(harness))
        {
            var state = world.BlockUpdates.GetBlock(position);
            var offset = position - harness.Origin;
            var key = $"{offset.X},{offset.Y},{offset.Z}";
            if (!state.IsAir)
                blocks.Add(key + "=" + Text(state));

            if (world.GetResidentBlockEntity(position) is StructureBlockEntity entity)
            {
                saved.Add(key, Canonical(BlockEntityNbt.SaveData(entity)));
            }
            else if (world.GetResidentBlockEntity(position) is Obsidian.API.BlockEntities.BlockEntity other)
            {
                var data = BlockEntityNbt.Save(other)!;
                foreach (var field in new[] { "x", "y", "z", "keepPacked" })
                    data.Remove(field);

                blockEntities.Add(key, Canonical(data));
            }
        }

        var chat = new JsonArray();
        foreach (var packet in ((TestClient)harness.Operator.Client).SentPackets.OfType<SystemChatPacket>())
        {
            if (!packet.Overlay)
                chat.Add(StoredCommand.PlainText(packet.Message));
        }

        var files = new JsonObject();
        var folder = StructureBlockTemplates.For(world).Folder;
        if (Directory.Exists(folder))
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.nbt", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal))
            {
                using var stream = File.OpenRead(path);
                var reader = new NbtReader(stream, NbtCompression.GZip);
                files.Add(Path.GetRelativePath(folder, path).Replace('\\', '/'), Canonical(reader.ReadNextTag()!));
            }
        }

        var items = new JsonObject();
        foreach (var group in world.SnapshotEntities().OfType<ItemEntity>()
            .Where(entity => entity.Item is not null).GroupBy(entity => entity.Item!.Holder.UnlocalizedName)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            items[group.Key] = group.Sum(entity => entity.Item!.Count);
        }

        var entities = new JsonArray();
        foreach (var text in world.SnapshotEntities().OfType<Obsidian.Entities.ArmorStand>()
            .Select(entity => EntityText(entity, harness.Origin)).Order(StringComparer.Ordinal))
        {
            entities.Add(text);
        }

        return new JsonObject
        {
            ["blocks"] = blocks,
            ["saved"] = saved,
            ["blockEntities"] = blockEntities,
            ["chat"] = chat,
            ["files"] = files,
            ["entities"] = entities,
            ["items"] = items
        };
    }

    private static string EntityText(Obsidian.Entities.ArmorStand entity, Vector origin)
    {
        var relative = entity.Position - (VectorD)origin;
        return "minecraft:armor_stand " + Number(relative.X) + "," + Number(relative.Y) + "," + Number(relative.Z)
            + " yaw=" + Number(entity.Yaw);
    }

    private static string Number(double number) => number.ToString("0.0###############", CultureInfo.InvariantCulture);

    private static JsonNode Canonical(INbtTag tag)
    {
        if (tag is NbtCompound compound)
        {
            var result = new JsonObject();
            foreach (var (key, child) in compound.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                result.Add(key, Canonical(child));

            return result;
        }

        if (tag is NbtList list)
            return new JsonArray(list.Select(Canonical).ToArray());

        if (tag is NbtArray<int> numbers)
            return new JsonArray(numbers.GetArray().Select(number => JsonValue.Create("3:" + number)).ToArray());

        if (tag is NbtTag<string> text)
            return JsonValue.Create(text.Value)!;

        var scalar = tag switch
        {
            NbtTag<bool> flag => flag.Value ? "1b" : "0b",
            NbtTag<byte> number => unchecked((sbyte)number.Value).ToString(CultureInfo.InvariantCulture) + "b",
            NbtTag<short> number => number.Value.ToString(CultureInfo.InvariantCulture) + "s",
            NbtTag<int> number => number.Value.ToString(CultureInfo.InvariantCulture),
            NbtTag<long> number => number.Value.ToString(CultureInfo.InvariantCulture) + "l",
            NbtTag<float> number => number.Value.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant() + "f",
            NbtTag<double> number => number.Value.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant() + "d",
            _ => throw new NotSupportedException(tag.GetType().Name)
        };
        return JsonValue.Create((int)tag.Type + ":" + scalar)!;
    }

    // Pin both sides of the generic serializer fields and wire drop, by exact case and action, so a changed
    // vanilla or Obsidian value fails instead of silently widening an exception.
    private static void CheckKnownGaps(JsonElement test, int action, JsonNode expected, JsonObject actual,
        List<string> differences)
    {
        var name = test.GetProperty("name").GetString();
        if (name == "strict_false" && action == 1 && test.TryGetProperty("itemGaps", out var items))
        {
            foreach (var gap in items.EnumerateObject())
            {
                var vanilla = JsonNode.Parse(gap.Value.GetProperty("vanilla").GetRawText());
                var obsidian = JsonNode.Parse(gap.Value.GetProperty("obsidian").GetRawText());
                Compare(vanilla, expected["items"]![gap.Name], "vanilla wire drop gap", differences);
                Compare(obsidian, actual["items"]![gap.Name], "Obsidian wire drop gap", differences);
                expected["items"]![gap.Name] = obsidian?.DeepClone();
            }
        }

        if (name != "entities_included_and_ignored")
            return;

        const string file = "test/structure/roundtrip.nbt";
        var wanted = (JsonObject)expected["files"]![file]!["entities"]![0]!["nbt"]!;
        var got = (JsonObject)actual["files"]![file]!["entities"]![0]!["nbt"]!;
        foreach (var gap in test.GetProperty("entitySaveGaps").EnumerateObject())
        {
            var vanilla = JsonNode.Parse(gap.Value.GetProperty("vanilla").GetRawText());
            var obsidian = JsonNode.Parse(gap.Value.GetProperty("obsidian").GetRawText());
            Compare(vanilla, wanted[gap.Name], "vanilla entity save gap " + gap.Name, differences);
            Compare(obsidian, got[gap.Name], "Obsidian entity save gap " + gap.Name, differences);
            wanted[gap.Name] = obsidian?.DeepClone();
        }
    }

    // Report every differing field, across every action and case. Nothing is pinned to an observed difference.
    private static void Compare(JsonNode? expected, JsonNode? actual, string path, List<string> differences)
    {
        if (expected is JsonObject wanted && actual is JsonObject got)
        {
            foreach (var key in wanted.Select(pair => pair.Key).Union(got.Select(pair => pair.Key)))
                Compare(wanted[key], got[key], path + "." + key, differences);
        }
        else if (!JsonNode.DeepEquals(expected, actual))
        {
            differences.Add($"{path}: expected {expected?.ToJsonString()}, got {actual?.ToJsonString()}");
        }
    }

    private static IEnumerable<Vector> Area(Harness harness)
    {
        for (var y = -1; y <= 5; y++)
        {
            for (var z = -6; z <= 8; z++)
            {
                for (var x = -6; x <= 8; x++)
                    yield return harness.Origin + new Vector(x, y, z);
            }
        }
    }

    private static Vector Position(JsonElement array) =>
        new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    // Fills the operator list in memory, without writing ops.json in the developer's configuration directory.
    private static void MakeOperator(Server server, ServerPlayer player, int permission = 4)
    {
        var field = typeof(OperatorList).GetField("operators", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var entries = (IList)field.GetValue(server.Operators)!;
        var type = entries.GetType().GetGenericArguments()[0];
        var entry = Activator.CreateInstance(type)!;
        type.GetProperty("Uuid")!.SetValue(entry, player.Uuid);
        type.GetProperty("Username")!.SetValue(entry, player.Username);
        type.GetProperty("Level")!.SetValue(entry, permission);
        entries.Add(entry);
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

}
