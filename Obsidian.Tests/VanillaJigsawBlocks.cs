#nullable enable
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Utilities;
using Obsidian.API;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Net;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Redstone;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using Xunit.Abstractions;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>Replays every jigsaw capture without output exceptions or pins.</summary>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaJigsawBlocks(ITestOutputHelper output)
{
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace
        | BlockUpdateFlags.SkipBlockEntitySideEffects | BlockUpdateFlags.SuppressDrops;

    [Fact]
    public async Task ReplayJigsawBlockReference()
    {
        using var stream = typeof(VanillaJigsawBlocks).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.jigsaw_blocks.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());
        await using var context = TestServerFactory.CreateServer($"jigsaw-blocks-{Guid.NewGuid():N}");
        var world = context.World;
        var origin = Position(root.GetProperty("origin"));
        world.LevelData.RandomSeed = root.GetProperty("seed").GetInt64();
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";
        for (var x = -9; x <= 9; x++)
        {
            for (var z = -9; z <= 9; z++)
                WorldBlockEntities.AddEmptyChunk(world, x, z);
        }

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Operator",
            (VectorD)origin + new VectorD(.5, 0, -8.5));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        MakeOperator(context.Server, player);
        var features = new FeatureRegistry(WorldgenData.Vanilla);
        var files = root.GetProperty("pools").EnumerateObject().Select(pool =>
            new StructureFile(PluginStructures.TemplatePool, pool.Name, "jigsaw capture", pool.Value)).ToArray();
        var templates = BuildTemplates(world, origin, root);
        PluginStructures.Apply(files, templates, features.AsFeatureSet(), [], NullLogger.Instance);
        try
        {
            var cases = root.GetProperty("cases").EnumerateArray().ToArray();
            await FixtureCases.AssertAllAsync(cases, test => test.GetProperty("name").GetString()!,
                test => test.TryGetProperty("recordChunks", out _)
                    ? JigsawBlockSafety.ReplayChunkAccessAsync(root, test)
                    : ReplayAsync(context, player, origin, test));
            output.WriteLine($"Replayed {cases.Length} jigsaw block cases without pins.");
        }
        finally
        {
            PluginStructures.Apply([], [], features.AsFeatureSet(), [], NullLogger.Instance);
        }
    }

    internal static TemplateFile[] BuildTemplates(World world, Vector origin, JsonElement root)
    {
        var source = origin + new Vector(256, 0, 256);
        WorldBlockEntities.AddEmptyChunk(world, source.X >> 4, source.Z >> 4);
        var templates = new List<TemplateFile>();
        foreach (var template in root.GetProperty("templates").EnumerateObject())
        {
            var size = Position(template.Value.GetProperty("size"));
            foreach (var pos in Area(source, Vector.Zero, size - 1))
                world.BlockUpdates.SetBlock(pos, BlocksRegistry.Air, Quiet);

            foreach (var block in template.Value.GetProperty("blocks").EnumerateArray())
                Place(world, source, block);

            var captured = StructureTemplate.Capture(world, source, size, false, string.Empty);
            if (template.Value.TryGetProperty("ignoreAir", out _))
                captured = WithoutAir(captured);

            templates.Add(new TemplateFile(template.Name, "jigsaw capture", captured));
        }

        return templates.ToArray();
    }

    private static StructureTemplate WithoutAir(StructureTemplate template)
    {
        var tag = template.ToNbt();
        var palette = (NbtList)tag["palette"];
        var entries = (NbtList)tag["blocks"];
        foreach (var entry in entries.OfType<NbtCompound>().ToArray())
        {
            var state = (NbtCompound)palette[entry.GetInt("state")];
            if (state.GetString("id") is "minecraft:air" or "minecraft:void_air")
                entries.Remove(entry);
        }

        using var buffer = new MemoryStream();
        using (var writer = new NbtWriterStream(buffer))
            writer.WriteTag(tag);

        using var input = new MemoryStream(buffer.ToArray());
        return StructureTemplate.Load(input, NbtCompression.None);
    }

    internal static void Place(World world, Vector origin, JsonElement placement)
    {
        var pos = origin + Position(placement.GetProperty("offset"));
        var state = VanillaSulfurBlocks.ParseState(placement.GetProperty("state").GetString()!);
        world.BlockUpdates.SetBlock(pos, state, Quiet);
        if (placement.TryGetProperty("nbt", out var nbt))
            ((BlockEntity)world.GetResidentBlockEntity(pos)!).Load(SnbtReader.ReadCompound(nbt.GetString()!));
    }

    private static async Task ReplayAsync(RealServerContext context, ServerPlayer player,
        Vector origin, JsonElement test)
    {
        var world = context.World;
        var min = Position(test.GetProperty("min"));
        var max = Position(test.GetProperty("max"));
        foreach (var pos in Area(origin, min, max))
            world.BlockUpdates.SetBlock(pos, BlocksRegistry.Air, Quiet);

        foreach (var block in test.GetProperty("setup").EnumerateArray())
            Place(world, origin, block);

        var index = 0;
        foreach (var action in test.GetProperty("actions").EnumerateArray())
        {
            var permission = action.TryGetProperty("permission", out var op) ? op.GetInt32() : 4;
            SetPermission(context, permission);
            player.GameMode = action.TryGetProperty("mode", out var mode)
                ? Enum.Parse<GameMode>(mode.GetString()!, true) : GameMode.Creative;
            var pos = origin + (action.TryGetProperty("offset", out var at) ? Position(at) : Vector.Zero);
            var observed = new JsonObject();
            if (action.TryGetProperty("edit", out var edit))
            {
                await EditPacket(pos, edit).HandleAsync(context.Server, player);
            }
            else if (action.TryGetProperty("generate", out var generate))
            {
                var packet = GeneratePacket(pos, generate);
                observed["levels"] = packet.Levels;
                var failed = false;
                try
                {
                    await packet.HandleAsync(context.Server, player);
                }
                catch (InvalidOperationException)
                {
                    failed = true;
                }

                observed["error"] = failed;
            }
            else if (action.TryGetProperty("reload", out _))
            {
                var entity = (BlockEntity)world.GetResidentBlockEntity(pos)!;
                var tag = BlockEntityNbt.Save(entity)!;
                var restored = BlockEntityNbt.Load(tag, entity.Id, pos);
                ((IRedstoneLevel)world).SetBlockEntityAt(pos, restored);
            }
            else if (action.TryGetProperty("use", out _))
            {
                var block = world.BlockUpdates.GetBlock(pos);
                var behavior = BlocksRegistry.GetUseWithoutItemBehavior(block.RegistryId);
                var result = UseResult.Pass;
                if (behavior is not null)
                {
                    result = await behavior.UseWithoutItemAsync(new BlockUse
                    {
                        Server = context.Server,
                        Level = world,
                        Player = player,
                        Position = pos,
                        Block = block,
                        BlockEntity = world.GetResidentBlockEntity(pos),
                        Face = BlockFace.Up,
                        Cursor = new VectorD(.5),
                        Hand = InteractionHand.MainHand,
                        HandSlot = player.CurrentHeldItemSlot,
                        Item = null,
                        UsedItem = null,
                        Sequence = 0
                    });
                }

                observed["consumes"] = result.ConsumesAction();
            }
            else if (action.TryGetProperty("placement", out var placement))
            {
                var yaw = placement.GetProperty("yaw").GetSingle();
                player.Yaw = yaw;
                var horizontal = new[] { BlockFace.South, BlockFace.West, BlockFace.North, BlockFace.East }[(int)yaw / 90];
                var state = PlacementBehaviors.GetState(new BlockPlaceContext
                {
                    Level = world.BlockUpdates,
                    Block = BlocksRegistry.Jigsaw,
                    Player = player,
                    Hand = InteractionHand.MainHand,
                    Item = null!,
                    Position = pos,
                    ClickedPosition = pos,
                    ClickedFace = Enum.Parse<BlockFace>(placement.GetProperty("face").GetString()!, true),
                    ClickedAt = new VectorD(.5),
                    ReplacesClicked = true,
                    HorizontalDirection = horizontal,
                    NearestLookDirection = horizontal
                });
                observed["placement"] = Text(state!);
            }

            observed["world"] = Checkpoint(world, origin, min, max);
            var expected = JsonNode.Parse(test.GetProperty("checkpoints")[index].GetRawText());
            Assert.True(JsonNode.DeepEquals(expected, observed),
                $"checkpoint {index}: expected {expected}, got {observed}");
            index++;
        }
    }

    internal static SetJigsawBlockPacket EditPacket(Vector position, JsonElement edit)
    {
        using var buffer = new NetworkBuffer();
        buffer.WritePosition(position);
        buffer.WriteString(edit.GetProperty("name").GetString()!);
        buffer.WriteString(edit.GetProperty("target").GetString()!);
        buffer.WriteString(edit.GetProperty("pool").GetString()!);
        buffer.WriteString(edit.GetProperty("finalState").GetString()!);
        buffer.WriteString(edit.GetProperty("joint").GetString()!);
        buffer.WriteVarInt(edit.GetProperty("selectionPriority").GetInt32());
        buffer.WriteVarInt(edit.GetProperty("placementPriority").GetInt32());
        return SetJigsawBlockPacket.Deserialize(buffer.AsSpan(0, buffer.Size).ToArray());
    }

    internal static JigsawGeneratePacket GeneratePacket(Vector position, JsonElement generate)
    {
        using var buffer = new NetworkBuffer();
        buffer.WritePosition(position);
        buffer.WriteVarInt(generate.GetProperty("levels").GetInt32());
        buffer.WriteBoolean(generate.GetProperty("keepJigsaws").GetBoolean());
        return JigsawGeneratePacket.Deserialize(buffer.AsSpan(0, buffer.Size).ToArray());
    }

    internal static JsonObject Checkpoint(World world, Vector origin, Vector min, Vector max)
    {
        var blocks = new JsonArray();
        var saved = new JsonObject();
        var updates = new JsonObject();
        foreach (var pos in Area(origin, min, max))
        {
            var block = world.BlockUpdates.GetBlock(pos);
            var offset = pos - origin;
            var key = $"{offset.X},{offset.Y},{offset.Z}";
            if (!block.IsAir)
                blocks.Add(key + "=" + Text(block));

            if (world.GetResidentBlockEntity(pos) is JigsawBlockEntity entity)
            {
                var stored = BlockEntityNbt.Save(entity)!;
                foreach (var field in new[] { "id", "x", "y", "z", "keepPacked" })
                    stored.Remove(field);

                saved[key] = Canonical(stored);
                updates[key] = Canonical(entity.GetUpdateData());
            }
        }

        return new JsonObject { ["blocks"] = blocks, ["saved"] = saved, ["updates"] = updates };
    }

    private static IEnumerable<Vector> Area(Vector origin, Vector min, Vector max)
    {
        for (var z = min.Z; z <= max.Z; z++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var x = min.X; x <= max.X; x++)
                    yield return origin + new Vector(x, y, z);
            }
        }
    }

    private static void SetPermission(RealServerContext context, int level)
    {
        var field = typeof(OperatorList).GetField("operators", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var entries = (IList)field.GetValue(context.Server.Operators)!;
        var entry = entries[0]!;
        entry.GetType().GetProperty("Level")!.SetValue(entry, level);
        entries[0] = entry;
    }

    internal static JsonNode Canonical(INbtTag tag)
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

    private static Vector Position(JsonElement array) =>
        new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    // Fills the operator list in memory, without writing ops.json in the developer's configuration directory.
    internal static void MakeOperator(Server server, ServerPlayer player, int permission = 4)
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
