using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.API.Inventory;
using Obsidian.WorldData;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

public sealed class VanillaMoreBlocks(ITestOutputHelper output)
{
    [Fact]
    public async Task ReplayConstructionAndMusicReference()
    {
        using var stream = typeof(VanillaMoreBlocks).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.more_blocks.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());
        await FixtureCases.AssertAllAsync(cases, probe => probe.GetProperty("name").GetString()!, Replay);
        output.WriteLine($"Replayed {cases.Length} construction and music cases.");
    }

    private static async Task Replay(JsonElement probe)
    {
        await using var context = TestServerFactory.CreateServer($"more-blocks-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.GameRules["spawn_mobs"] = "false";
        world.LevelData.GameRules["random_tick_speed"] = "0";
        await BlockUpdates.AddChunksAsync(world);
        foreach (var placement in probe.GetProperty("setup").EnumerateArray())
        {
            world.BlockUpdates.SetBlock(Position(placement.GetProperty("pos")),
                Parse(placement.GetProperty("state").GetString()!),
                BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        }

        var origin = Position(probe.GetProperty("origin"));
        var kind = probe.GetProperty("kind").GetString();
        if (kind == "placement")
        {
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Placer");
            player.Yaw = probe.GetProperty("rotation").GetSingle();
            var look = Face(probe.GetProperty("look").GetString()!);
            player.Pitch = look == BlockFace.Up ? -90 : 0;
            if (look == BlockFace.Down)
            {
                player.Pitch = 90;
            }

            var block = Parse(probe.GetProperty("block").GetString()!);
            var face = Face(probe.GetProperty("face").GetString()!);
            var cursor = Location(probe.GetProperty("cursor"));
            var replaces = probe.GetProperty("replacesClicked").GetBoolean();
            // The probe has already chosen the sign variant; item-side variant selection is a separate step.
            if (block.BlockClass() == "StandingSignBlock")
            {
                AssertPlacement(probe, Signs.GetStandingState(world.BlockUpdates, block, origin, player));
                return;
            }

            var behavior = PlacementBehaviors.For(block);
            var placed = behavior is null
                ? VanillaPlacement.GetState(world.BlockUpdates, block, origin, player,
                    block.BlockClass() == "CeilingHangingSignBlock" ? BlockFace.Down : face, cursor, replaces)
                : behavior.GetStateForPlacement(new BlockPlaceContext
                {
                    Level = world.BlockUpdates,
                    Block = block,
                    Player = player,
                    Item = null!,
                    Hand = InteractionHand.MainHand,
                    Position = origin,
                    ClickedPosition = origin,
                    ClickedFace = face,
                    ClickedAt = cursor,
                    ReplacesClicked = replaces,
                    HorizontalDirection = Face(probe.GetProperty("horizontal").GetString()!),
                    NearestLookDirection = Face(probe.GetProperty("nearest").GetString()!)
                });
            AssertPlacement(probe, placed);
            return;
        }

        if (kind == "shape")
        {
            var side = Face(probe.GetProperty("direction").GetString()!);
            var neighbor = origin.Offset(side);
            world.BlockNotifications.Enqueue(new BlockChange(neighbor, BlocksRegistry.Air,
                world.BlockUpdates.GetBlock(neighbor)), BlockUpdateFlags.SkipOnPlace);
            await world.BlockNotifications.DrainAsync();
            AssertState(probe.GetProperty("updated"), world.BlockUpdates.GetBlock(origin));
            var chunk = Assert.IsType<Chunk>(await world.GetChunkAsync(origin.X >> 4, origin.Z >> 4));
            var ticks = chunk.BlockTicks.Pack().Where(tick => tick.Position == origin)
                .Select(tick => (Type: tick.Block, tick.Delay))
                .Concat(chunk.FluidTicks.Pack().Where(tick => tick.Position == origin)
                    .Select(tick => (Type: "minecraft:" + tick.Type.ToString().ToLowerInvariant(), tick.Delay)))
                .OrderBy(tick => tick.Type).ToArray();
            var expected = probe.GetProperty("ticks").EnumerateArray()
                .Select(tick => (
                    Type: tick.GetProperty("type").GetString()!,
                    Delay: tick.GetProperty("delay").GetInt32()))
                .OrderBy(tick => tick.Type).ToArray();
            Assert.Equal(expected, ticks);
            return;
        }

        if (kind == "placed_by")
        {
            VanillaPlacement.OnPlacedByPlayer(world.BlockUpdates, origin, world.BlockUpdates.GetBlock(origin));
            AssertState(probe.GetProperty("upper"), world.BlockUpdates.GetBlock(origin + Vector.Up));
            return;
        }

        if (kind == "use")
        {
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User");
            player.Yaw = probe.GetProperty("rotation").GetSingle();
            var sound = VanillaPlacement.Use(world.BlockUpdates, origin, world.BlockUpdates.GetBlock(origin), player);
            AssertState(probe.GetProperty("updated"), world.BlockUpdates.GetBlock(origin));
            var sounds = probe.GetProperty("sounds").EnumerateArray().ToArray();
            Assert.Single(sounds);
            Assert.Equal(sounds[0].GetProperty("sound").GetString(), sound);
            return;
        }

        if (kind == "use_item")
        {
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "HeadPlacer",
                new VectorD(0.5, 72, 3));
            player.GameMode = GameMode.Survival;
            player.Yaw = probe.GetProperty("rotation").GetSingle();
            player.Sneaking = probe.GetProperty("sneaking").GetBoolean();
            player.Pitch = 45;
            var item = ItemsRegistry.Get(probe.GetProperty("item").GetString()!);
            player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(item, 2));
            var side = Face(probe.GetProperty("face").GetString()!);
            await BlockUpdates.UseOn(origin, side).HandleAsync(context.Server, player);
            await world.BlockNotifications.DrainAsync();
            Assert.Equal(probe.GetProperty("note").GetInt32(),
                int.Parse(world.BlockUpdates.GetBlock(origin).GetProperty("note")!));
            AssertState(probe.GetProperty("target"), world.BlockUpdates.GetBlock(origin.Offset(side)));
            Assert.Equal(probe.GetProperty("remaining").GetInt32(),
                player.Inventory.GetItem(player.CurrentHeldItemSlot)?.Count ?? 0);
            return;
        }

        world.GrowthRandom.SetSeed(probe.TryGetProperty("seed", out var seed) ? seed.GetInt64() : 123);
        if (kind == "strip")
        {
            var result = ItemUseOnBehaviors.Evaluate("minecraft:axe", world.BlockUpdates.GetBlock,
                origin, BlockFace.Up);
            var expected = probe.GetProperty("transformed");
            if (expected.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(result);
            }
            else
            {
                Assert.NotNull(result);
                AssertState(expected, result.State);
                Assert.Equal(probe.GetProperty("sound").GetString(), result.Sound);
                Assert.Equal(probe.GetProperty("damage").GetInt32(), result.Damage);
            }
        }
        else if (kind == "leaf_tick")
        {
            var state = world.BlockUpdates.GetBlock(origin);
            world.BlockUpdates.RunScheduledTick(origin, state.UnlocalizedName);
            AssertState(probe.GetProperty("updated"), world.BlockUpdates.GetBlock(origin));
        }
        else if (kind == "block_events")
        {
            await ReplayBlockEvents(probe, context, origin);
            return;
        }
        else if (kind == "note_lifecycle")
        {
            var listener = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Listener",
                new VectorD(8, 80, 8));
            world.Players.TryAdd(listener.Uuid, listener);
            var client = Assert.IsType<TestClient>(listener.Client);
            var phaseIndex = 0;
            foreach (var action in probe.GetProperty("actions").EnumerateArray())
            {
                var position = action.TryGetProperty("pos", out var coordinates) ? Position(coordinates) : origin;
                switch (action.GetProperty("action").GetString())
                {
                    case "set":
                        world.BlockUpdates.SetBlock(position, Parse(action.GetProperty("state").GetString()!),
                            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
                        break;
                    case "play":
                        world.PlayNoteBlock(position);
                        break;
                    case "phase":
                        var phase = probe.GetProperty("phases")[phaseIndex++];
                        Assert.Equal(phase.GetProperty("queued").GetInt32(), world.PendingBlockEvents);
                        Assert.Equal(phase.GetProperty("soundsBefore").GetInt32(),
                            client.SentPackets.OfType<SoundPacket>().Count());
                        Assert.Equal(phase.GetProperty("eventsBefore").GetInt32(),
                            client.SentPackets.OfType<BlockEventPacket>().Count());
                        await world.DoWorldTickAsync();
                        Assert.Equal(phase.GetProperty("pendingAfter").GetInt32(), world.PendingBlockEvents);
                        AssertNoteSounds(phase.GetProperty("sounds"), client);
                        var events = client.SentPackets.OfType<BlockEventPacket>().ToArray();
                        var expected = phase.GetProperty("events").EnumerateArray().ToArray();
                        Assert.Equal(expected.Length, events.Length);
                        for (var index = 0; index < expected.Length; index++)
                        {
                            Assert.Equal(Position(expected[index].GetProperty("pos")), events[index].Position);
                            Assert.Equal(BlockIds.NoteBlock, events[index].BlockType);
                            Assert.Equal(expected[index].GetProperty("id").GetInt32(), events[index].ActionId);
                            Assert.Equal(expected[index].GetProperty("parameter").GetInt32(),
                                events[index].ActionParam);
                        }

                        break;
                    default:
                        throw new InvalidOperationException($"Unknown lifecycle action: {action}");
                }
            }
        }
        else if (kind == "note")
        {
            var listener = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Listener",
                new VectorD(8, 80, 8));
            world.Players.TryAdd(listener.Uuid, listener);
            var client = Assert.IsType<TestClient>(listener.Client);
            world.PlayNoteBlock(origin);
            await world.ProcessBlockEventsAsync();
            AssertNoteSounds(probe.GetProperty("sounds"), client);

            var events = client.SentPackets.OfType<BlockEventPacket>().ToArray();
            Assert.Equal(probe.GetProperty("triggered").GetBoolean() ? 1 : 0, events.Length);
            foreach (var packet in events)
            {
                var expected = probe.GetProperty("events")[0];
                Assert.Equal(origin, packet.Position);
                Assert.Equal(BlockIds.NoteBlock, packet.BlockType);
                Assert.Equal(expected.GetProperty("id").GetInt32(), packet.ActionId);
                Assert.Equal(expected.GetProperty("parameter").GetInt32(), packet.ActionParam);
            }
        }
        else
        {
            throw new InvalidOperationException($"Unknown probe kind: {kind}");
        }

        Assert.Equal(probe.GetProperty("nextRandom").GetInt32(), world.GrowthRandom.NextInt());
    }

    private static async Task ReplayBlockEvents(JsonElement probe, RealServerContext context, Vector origin)
    {
        var world = context.World;
        var listener = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Listener",
            new VectorD(8, 80, 8));
        world.Players.TryAdd(listener.Uuid, listener);
        var client = Assert.IsType<TestClient>(listener.Client);
        var phaseIndex = 0;
        foreach (var action in probe.GetProperty("actions").EnumerateArray())
        {
            var position = action.TryGetProperty("pos", out var coordinates) ? Position(coordinates) : origin;
            switch (action.GetProperty("action").GetString())
            {
                case "set":
                    world.BlockUpdates.SetBlock(position, Parse(action.GetProperty("state").GetString()!),
                        BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
                    break;
                case "play":
                    world.PlayNoteBlock(position);
                    break;
                case "ticking":
                    listener.GameMode = action.GetProperty("value").GetBoolean()
                        ? GameMode.Survival : GameMode.Spectator;
                    break;
                case "piston":
                    world.BlockNotifications.EnqueueNeighbor(position, position + Vector.Down);
                    await world.BlockNotifications.DrainAsync();
                    break;
                case "gateway_cooldown":
                    world.TryUseEndGateway(position);
                    break;
                case "spawner_reset":
                    var spawner = new MobSpawner(world);
                    var reset = typeof(MobSpawner).GetMethod("ResetSpawner",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    reset.Invoke(spawner, [await world.GetBlockEntityAsync(position), true]);
                    break;
                case "event":
                    world.QueueBlockEvent(position, world.BlockUpdates.GetBlock(position).RegistryId,
                        action.GetProperty("id").GetInt32(), action.GetProperty("parameter").GetInt32());
                    break;
                case "phase":
                    var phase = probe.GetProperty("phases")[phaseIndex++];
                    Assert.Equal(phase.GetProperty("queued").GetInt32(), world.PendingBlockEvents);
                    Assert.Equal(phase.GetProperty("soundsBefore").GetInt32(),
                        client.SentPackets.OfType<SoundPacket>().Count());
                    Assert.Equal(phase.GetProperty("eventsBefore").GetInt32(),
                        client.SentPackets.OfType<BlockEventPacket>().Count());
                    await world.DoWorldTickAsync();
                    Assert.Equal(phase.GetProperty("pendingAfter").GetInt32(), world.PendingBlockEvents);

                    // These mixed probes assert event/sound order and movement. The existing note references
                    // separately assert exact seeds; piston pitch/random consumption is outside this queue change.
                    var sounds = phase.GetProperty("sounds").EnumerateArray().ToArray();
                    Assert.Equal(sounds.Select(sound => sound.GetProperty("sound").GetString()),
                        client.SentPackets.OfType<SoundPacket>().Select(packet => packet.SoundLocation));
                    var events = client.SentPackets.OfType<BlockEventPacket>().ToArray();
                    var expected = phase.GetProperty("events").EnumerateArray().ToArray();
                    Assert.Equal(expected.Length, events.Length);
                    for (var index = 0; index < expected.Length; index++)
                    {
                        Assert.Equal(Position(expected[index].GetProperty("pos")), events[index].Position);
                        Assert.Equal(Parse(expected[index].GetProperty("block").GetString()!).RegistryId,
                            events[index].BlockType);
                        Assert.Equal(expected[index].GetProperty("id").GetInt32(), events[index].ActionId);
                        Assert.Equal(expected[index].GetProperty("parameter").GetInt32(), events[index].ActionParam);
                    }

                    var watch = probe.GetProperty("watch").EnumerateArray().ToArray();
                    for (var index = 0; index < watch.Length; index++)
                    {
                        AssertState(phase.GetProperty("states")[index],
                            world.BlockUpdates.GetBlock(Position(watch[index])));
                    }

                    break;
                default:
                    throw new InvalidOperationException($"Unknown block-event action: {action}");
            }
        }
    }

    private static void AssertNoteSounds(JsonElement expected, TestClient client)
    {
        var packets = client.SentPackets.OfType<SoundPacket>().ToArray();
        var sounds = expected.EnumerateArray().ToArray();
        Assert.Equal(sounds.Length, packets.Length);
        for (var index = 0; index < sounds.Length; index++)
        {
            Assert.Equal(sounds[index].GetProperty("sound").GetString(), packets[index].SoundLocation);
            Assert.Equal(sounds[index].GetProperty("volume").GetSingle(), packets[index].Volume);
            Assert.Equal(sounds[index].GetProperty("pitch").GetSingle(), packets[index].Pitch);
            Assert.Equal(sounds[index].GetProperty("seed").GetInt64(), packets[index].Seed);
            Assert.Equal(SoundCategory.Records, packets[index].Category);
            var position = sounds[index].TryGetProperty("pos", out var coordinates)
                ? Location(coordinates)
                : new VectorD(0.5, 72.5, 0.5);
            Assert.Equal(new SoundPosition(position.X, position.Y, position.Z), packets[index].SoundPosition);
        }
    }

    private static void AssertPlacement(JsonElement probe, IBlock? placed)
    {
        var expected = probe.GetProperty("placed");
        if (expected.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(placed);
        }
        else
        {
            Assert.NotNull(placed);
            AssertState(expected, placed);
        }
    }

    private static void AssertState(JsonElement expected, IBlock actual) =>
        Assert.True(Parse(expected.GetString()!).IsSameState(actual), $"expected {expected}, got {Format(actual)}");

    private static string Format(IBlock state) =>
        state.UnlocalizedName + "["
        + string.Join(',', state.GetProperties().Select(pair => $"{pair.Key}={pair.Value}")) + "]";

    private static IBlock Parse(string state) => VanillaSulfurBlocks.ParseState(state);

    private static BlockFace Face(string text) => Enum.Parse<BlockFace>(text, ignoreCase: true);

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());

    private static VectorD Location(JsonElement values) =>
        new(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble());
}
