using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Plugins;
using Obsidian.API.Registries;
using Obsidian.API.World;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Ticks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Block ticks run and save like vanilla's, block changes reach their neighbours in vanilla's order, falling blocks
/// fall on their scheduled tick, and a plugin's block hooks (placement, changes, updates, ticks) run until the plugin
/// unloads, through the probe plugin's <see cref="ProbeRelay"/> blocks.
/// </summary>
// Loading the probe plugin merges its tags into vanilla's, which PluginTagFiles changes too.
[Collection(nameof(PluginTagFiles))]
public sealed class BlockUpdates : IDisposable
{
    internal const int Ground = 69;

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-block-updates-{Guid.NewGuid():N}");

    public BlockUpdates() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    [Fact(DisplayName = "Block ticks save as vanilla's block_ticks, run in vanilla's order and keep it when held")]
    public void BlockTicksSaveAndRunInVanillaOrder()
    {
        // A chunk that doesn't tick yet keeps its ticks with delays, as a generating or saved chunk does.
        var saved = new ChunkTicks<string>();
        saved.Schedule(new Vector(1, 64, 1), "minecraft:sand", 2);
        saved.Schedule(new Vector(2, 64, 2), "minecraft:sand", 1);
        saved.Schedule(new Vector(3, 64, 3), "minecraft:gravel", 2, priority: -1);
        saved.Schedule(new Vector(4, 64, 4), "minecraft:sand", 2);
        saved.Schedule(new Vector(4, 64, 4), "minecraft:sand", 1);

        var output = new MemoryStream();
        using (var writer = new NbtWriterStream(output, ""))
        {
            saved.Write(writer, "block_ticks", name => name);
            writer.EndCompound();
            writer.TryFinish();
        }

        // The writer closes the stream it wrote.
        using var input = new MemoryStream(output.ToArray());
        var root = (NbtCompound)new NbtReader(input).ReadNextTag()!;
        var list = (NbtList)root["block_ticks"];

        // A position holds one tick per block, like vanilla: the second sand tick at 4 was ignored.
        Assert.Equal(4, list.Count);
        var gravel = list.Cast<NbtCompound>().Single(tick => tick.GetString("i") == "minecraft:gravel");
        Assert.Equal([3, 64, 3, 2, -1], new[] { "x", "y", "z", "t", "p" }.Select(gravel.GetInt));

        var loaded = new ChunkTicks<string>();
        loaded.Read(list, 0, 0, LevelBlockUpdates.TryParseTickType);

        var scheduler = new TickScheduler<string>();
        loaded.StartTicking(scheduler, key: 0);

        // Scheduled while ticking: due with the saved ones, but after them.
        loaded.Schedule(new Vector(5, 64, 5), "minecraft:sand", 2);

        var ran = new List<int>();
        var hold = false;
        bool Run(Vector position, string type)
        {
            if (hold)
                return false;

            ran.Add(position.X);
            return true;
        }

        // Game tick 1: the one due then.
        scheduler.Clock.Advance();
        scheduler.Tick(_ => true, Run);
        Assert.Equal([2], ran);

        loaded.Schedule(new Vector(6, 64, 6), "minecraft:sand", 2);

        // Game tick 2: held (a plugin's block while its plugin is away), the due ticks stay as they were scheduled.
        hold = true;
        scheduler.Clock.Advance();
        scheduler.Tick(_ => true, Run);
        var held = loaded.Pack().Single(tick => tick.Type == "minecraft:gravel");
        Assert.Equal((0, -1), (held.Delay, held.Priority));

        // Game tick 3: the held ticks first, as due before the one scheduled at game tick 1, by priority, then the
        // order they were scheduled in.
        hold = false;
        scheduler.Clock.Advance();
        scheduler.Tick(_ => true, Run);
        Assert.Equal([2, 3, 1, 4, 5, 6], ran);
        Assert.Equal(0, loaded.Count);

        // More unavailable ticks than the budget must not starve runnable ticks in the same chunk. Their original
        // timing, priority and order survive saving and restoring the held side list too.
        loaded.Schedule(new Vector(7, 64, 7), "probe:missing", 0);
        loaded.Schedule(new Vector(8, 64, 8), "probe:missing", 0);
        loaded.Schedule(new Vector(9, 64, 9), "probe:missing", 0, priority: -1);
        loaded.Schedule(new Vector(10, 64, 10), "minecraft:sand", 0);
        loaded.Schedule(new Vector(11, 64, 11), "minecraft:sand", 0);
        scheduler.Tick(_ => true, Run, maxTicks: 2, canRunType: type => type != "probe:missing");
        Assert.Equal([2, 3, 1, 4, 5, 6, 10, 11], ran);
        Assert.Equal(3, loaded.Count);
        Assert.True(loaded.Contains(new Vector(7, 64, 7), "probe:missing"));

        scheduler.Clock.Advance();
        scheduler.Tick(_ => true, Run, maxTicks: 2, canRunType: type => type != "probe:missing");
        Assert.All(loaded.Pack(), tick => Assert.Equal(-1, tick.Delay));
        loaded.StopTicking();
        Assert.Equal([-1, 0, 0], loaded.Pack().Select(tick => tick.Priority));
        loaded.StartTicking(scheduler, key: 0);
        scheduler.Tick(_ => true, Run, maxTicks: 2, canRunType: _ => true);
        scheduler.Tick(_ => true, Run, maxTicks: 2, canRunType: _ => true);
        Assert.Equal([2, 3, 1, 4, 5, 6, 10, 11, 9, 7, 8], ran);
        Assert.Equal(0, loaded.Count);
    }

    [Fact(
        DisplayName = "Players face plugin blocks as vanilla works out their look; a cancelled place keeps the item",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task PlacementUsesFacingAndTheEvent()
    {
        // Vanilla's yaw: 0 faces south, 90 west, 180 north, 270 east; pitch -90 looks up.
        Assert.Equal(BlockFace.South, PlacementDirections.Horizontal(10));
        Assert.Equal(BlockFace.West, PlacementDirections.Horizontal(90));
        Assert.Equal(BlockFace.North, PlacementDirections.Horizontal(-170));
        Assert.Equal(BlockFace.East, PlacementDirections.Nearest(-80, 20));
        Assert.Equal(BlockFace.Up, PlacementDirections.Nearest(0, -60));
        Assert.Equal(BlockFace.Down, PlacementDirections.Nearest(180, 50));
        Assert.Equal(BlockFace.North, PlacementDirections.Nearest(180, 40));

        await using var context = TestServerFactory.CreateServer($"block-placement-{Guid.NewGuid():N}");
        var world = context.World;
        await AddChunksAsync(world);
        await using var manager = CreatePluginManager(context);
        var container = await this.LoadProbeAsync(manager);

        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:relay", out var relay));
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Placer");
        player.GameMode = GameMode.Survival;
        player.Yaw = 90;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(relay.Item!, 2));

        var cancel = true;
        var events = 0;
        Func<ValueTask> meanwhile = null;
        context.EventDispatcher.RegisterEvent(null, (Func<BlockPlaceEventArgs, ValueTask>)(async placing =>
        {
            events++;
            Assert.Same(player, placing.Player);
            if (cancel)
                placing.Cancel();

            if (meanwhile is not null)
                await meanwhile();
        }));

        // Clicking the ground's top places the block above it, facing the player (east, for a player facing west).
        var ground = new Vector(4, Ground, 4);
        var above = ground + Vector.Up;
        await UseOn(ground, BlockFace.Up).HandleAsync(context.Server, player);

        Assert.Equal(1, events);
        Assert.True(world.GetLoadedChunk(0, 0)!.GetBlock(above).IsAir);
        Assert.Equal(2, player.GetHeldItem()?.Count);

        cancel = false;
        await UseOn(ground, BlockFace.Up).HandleAsync(context.Server, player);

        var placed = world.GetLoadedChunk(0, 0)!.GetBlock(above);
        Assert.True(relay.Is(placed));
        Assert.Equal("east", relay.GetValue(placed, "facing"));
        Assert.Equal(1, player.GetHeldItem()?.Count);

        // A handler that fills the target while the event runs keeps the block from being placed and the item from
        // being used; the client is sent the block there.
        var client = (TestClient)player.Client;
        var filled = new Vector(6, Ground + 1, 6);
        meanwhile = () => world.SetBlockAsync(filled, BlocksRegistry.Stone);
        client.ClearPackets();
        await UseOn(filled + Vector.Down, BlockFace.Up).HandleAsync(context.Server, player);

        Assert.True(world.GetLoadedChunk(0, 0)!.GetBlock(filled).Is(BlockIds.Stone));
        Assert.Equal(1, player.GetHeldItem()?.Count);
        Assert.Contains(
            client.SentPackets.OfType<BlockUpdatePacket>(),
            packet => packet.Position == filled && packet.BlockId == BlocksRegistry.Stone.GetHashCode());

        // Vanilla's redstone components go down as vanilla places them: a lever on the floor facing the way the
        // player does, a torch clicked onto a block's side on that side, and redstone dust as a cross.
        meanwhile = null;
        async Task<IBlock> PlaceVanillaAsync(string item, Vector clicked, BlockFace face)
        {
            player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Get(item), 1));
            await UseOn(clicked, face).HandleAsync(context.Server, player);
            return world.GetLoadedChunk(0, 0)!.GetBlock(clicked + face.ToVector());
        }

        var wall = new Vector(12, Ground + 1, 4);
        await world.SetBlockAsync(wall, BlocksRegistry.Stone);

        var lever = await PlaceVanillaAsync("minecraft:lever", new Vector(10, Ground, 4), BlockFace.Up);
        Assert.Equal(("floor", "west"), (lever.GetProperty("face"), lever.GetProperty("facing")));
        var torch = await PlaceVanillaAsync("minecraft:redstone_torch", wall, BlockFace.East);
        Assert.Equal(("minecraft:redstone_wall_torch", "east"), (torch.UnlocalizedName, torch.GetProperty("facing")));
        var dust = await PlaceVanillaAsync("minecraft:redstone", new Vector(14, Ground, 8), BlockFace.Up);
        Assert.All(["north", "east", "south", "west"], side => Assert.Equal("side", dust.GetProperty(side)));

        await manager.UnloadPluginAsync(container);
    }

    [Fact(DisplayName = "A plugin block's hooks run in vanilla's update order, its ticks run; unloading drops them",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginBlockHooksFollowVanillaOrder()
    {
        await using var context = TestServerFactory.CreateServer($"block-hooks-{Guid.NewGuid():N}");
        var world = context.World;
        await AddChunksAsync(world);
        await using var manager = CreatePluginManager(context);
        var container = await this.LoadProbeAsync(manager);

        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:relay", out var relay));
        var shapeProperties = new BlockProperties
        {
            StateProperties =
            [
                StateProperty.Enum("shape", ["upper", "split", "center", "rim", "side_center", "side_rim", "empty"])
            ],
            StateShapes = values =>
            {
                BoundingBox Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
                    new(new VectorD(x0, y0, z0), new VectorD(x1, y1, z1));

                BoundingBox[] boxes = values["shape"] switch
                {
                    "upper" => [Box(0, 0.5, 0, 1, 1, 1)],
                    "split" => [Box(0, 0, 0, 0.5, 1, 1), Box(0.5, 0, 0, 1, 1, 1)],
                    "center" => [Box(7d / 16, 0, 7d / 16, 9d / 16, 1, 9d / 16)],
                    "side_center" => [Box(0, 7d / 16, 7d / 16, 1, 9d / 16, 9d / 16)],
                    "rim" =>
                    [
                        Box(0, 0, 0, 1, 1, 2d / 16),
                        Box(0, 0, 14d / 16, 1, 1, 1),
                        Box(0, 0, 2d / 16, 2d / 16, 1, 14d / 16),
                        Box(14d / 16, 0, 2d / 16, 1, 1, 14d / 16)
                    ],
                    "side_rim" =>
                    [
                        Box(0, 0, 0, 1, 1, 2d / 16),
                        Box(0, 0, 14d / 16, 1, 1, 1),
                        Box(0, 0, 2d / 16, 1, 2d / 16, 14d / 16),
                        Box(0, 14d / 16, 2d / 16, 1, 1, 14d / 16)
                    ],
                    _ => []
                };
                return new BlockShapes(boxes, boxes);
            }
        };
        var support = BlocksRegistry.Register("probe:shape_support", shapeProperties);
        support.Settings = BlockSettings.From(shapeProperties, support);
        var upper = support.With(support.DefaultState, "shape", "upper");
        Assert.True(upper.IsFaceSturdy(BlockFace.Up));
        Assert.True(upper.HasFullTopCollisionFace());
        Assert.True(upper.IsTopCenterSturdy());
        Assert.True(upper.IsTopRigidSturdy());
        Assert.False(upper.IsFaceSturdy(BlockFace.Down));
        Assert.False(upper.IsSolidRender());
        var split = support.With(support.DefaultState, "shape", "split");
        Assert.True(split.IsCollisionShapeFullBlock());
        Assert.True(split.HasBlockCollisionShape());
        Assert.True(split.IsSolidRender());
        Assert.All(Enum.GetValues<BlockFace>(), face => Assert.True(split.IsFaceSturdy(face)));
        var centerSupport = support.With(support.DefaultState, "shape", "center");
        Assert.True(centerSupport.IsTopCenterSturdy());
        Assert.True(centerSupport.IsBottomCenterSturdy());
        Assert.False(centerSupport.IsTopRigidSturdy());
        var rim = support.With(support.DefaultState, "shape", "rim");
        Assert.True(rim.IsTopRigidSturdy());
        Assert.False(rim.IsTopCenterSturdy());
        Assert.False(rim.IsFaceSturdy(BlockFace.Up));
        Assert.True(support.With(support.DefaultState, "shape", "empty").HasEmptyCollision());

        var chunk = world.GetLoadedChunk(0, 0)!;
        ILevel publicLevel = world;
        IBlockLevel hookLevel = world.BlockUpdates;
        var supportPosition = new Vector(2, Ground + 1, 2);
        (string Shape, BlockFace Face, bool Full, bool Center, bool Rigid)[] supportCases =
        [
            ("upper", BlockFace.Up, true, true, true),
            ("upper", BlockFace.Down, false, false, false),
            ("split", BlockFace.North, true, true, true),
            ("center", BlockFace.Down, false, true, false),
            ("rim", BlockFace.Up, false, false, true),
            ("side_center", BlockFace.West, false, true, false),
            ("side_rim", BlockFace.East, false, false, true),
            ("empty", BlockFace.Up, false, false, false)
        ];
        foreach (var entry in supportCases)
        {
            chunk.SetBlock(supportPosition, support.With(support.DefaultState, "shape", entry.Shape));
            Assert.Equal(entry.Full, publicLevel.IsFaceSturdy(supportPosition, entry.Face));
            Assert.Equal(entry.Full, hookLevel.IsFaceSturdy(supportPosition, entry.Face));
            Assert.Equal(entry.Center, publicLevel.IsFaceSturdy(supportPosition, entry.Face, SupportType.Center));
            Assert.Equal(entry.Center, hookLevel.IsFaceSturdy(supportPosition, entry.Face, SupportType.Center));
            Assert.Equal(entry.Rigid, publicLevel.IsFaceSturdy(supportPosition, entry.Face, SupportType.Rigid));
            Assert.Equal(entry.Rigid, hookLevel.IsFaceSturdy(supportPosition, entry.Face, SupportType.Rigid));
        }

        // Vanilla soul sand's support shape is full even though its collision box is only 14/16 high.
        chunk.SetBlock(supportPosition, BlocksRegistry.SoulSand);
        Assert.All(Enum.GetValues<SupportType>(), mode =>
            Assert.True(publicLevel.IsFaceSturdy(supportPosition, BlockFace.Up, mode)));

        // Captured vanilla 1.21.11: a hopper supports the top rim and bottom center, but no full face.
        chunk.SetBlock(supportPosition, BlocksRegistry.Hopper);
        Assert.False(publicLevel.IsFaceSturdy(supportPosition, BlockFace.Up));
        Assert.False(publicLevel.IsFaceSturdy(supportPosition, BlockFace.Up, SupportType.Center));
        Assert.True(publicLevel.IsFaceSturdy(supportPosition, BlockFace.Up, SupportType.Rigid));
        Assert.False(hookLevel.IsFaceSturdy(supportPosition, BlockFace.Down));
        Assert.True(hookLevel.IsFaceSturdy(supportPosition, BlockFace.Down, SupportType.Center));
        Assert.False(hookLevel.IsFaceSturdy(supportPosition, BlockFace.Down, SupportType.Rigid));

        // One snow layer has no collision, yet its support shape has a full bottom face.
        chunk.SetBlock(supportPosition, BlocksRegistry.Snow);
        Assert.All(Enum.GetValues<SupportType>(), mode =>
        {
            Assert.True(publicLevel.IsFaceSturdy(supportPosition, BlockFace.Down, mode));
            Assert.False(publicLevel.IsFaceSturdy(supportPosition, BlockFace.Up, mode));
        });

        chunk.SetBlock(supportPosition, BlocksRegistry.Air);
        Assert.False(publicLevel.IsFaceSturdy(supportPosition, BlockFace.Up));
        var unloaded = new Vector(1600, Ground, 1600);
        Assert.All(Enum.GetValues<SupportType>(), mode =>
        {
            Assert.False(publicLevel.IsFaceSturdy(unloaded, BlockFace.Up, mode));
            Assert.False(hookLevel.IsFaceSturdy(unloaded, BlockFace.Up, mode));
            Assert.False(publicLevel.IsFaceSturdy(new Vector(2, int.MaxValue, 2), BlockFace.Up, mode));
        });
        Assert.Null(world.GetLoadedChunk(100, 100));

        var calls = ProbeRelay.Reset();

        // Count random ticks before the other scenarios leave relays in another section. Every draw must hit a
        // relay in this full section, so the count is exact regardless of the level's random starting position.
        var player = new FakePlayer { Position = new VectorD(8, Ground + 1, 8) };
        Assert.True(world.TryAddPlayer(player));
        try
        {
            FillSection(world, relay.DefaultState);
            for (var tick = 0; tick < 32; tick++)
            {
                calls.Clear();
                world.TickRandomBlocks();
                Assert.Equal(world.LevelData.GetIntegerRule("random_tick_speed", 3), calls.Count);
                Assert.All(calls, call => Assert.StartsWith("random", call));
            }

            FillSection(world, BlocksRegistry.Air);
        }
        finally
        {
            world.TryRemovePlayer(player);
        }

        // Scheduled ticks now need simulation too; keep this circuit active after the random-tick player leaves.
        using var simulation = world.AddChunkTicket(0, 0);
        var center = new Vector(8, Ground + 3, 8);
        Vector[] around = [Vector.Up, Vector.Down, Vector.North, Vector.South, Vector.West, Vector.East];
        foreach (var offset in around)
            chunk.SetBlock(center + offset, relay.DefaultState);

        calls.Clear();
        await world.SetBlockAsync(center, BlocksRegistry.Stone, BlockUpdateFlags.All);

        // Vanilla's neighborChanged order (west, east, down, up, north, south), then its updateShape order (west,
        // east, north, south, down, up), each told the side the change was on.
        string[] expected =
        [
            "neighbor 7,72,8 from air", "neighbor 9,72,8 from air", "neighbor 8,71,8 from air",
            "neighbor 8,73,8 from air", "neighbor 8,72,7 from air", "neighbor 8,72,9 from air",
            "shape 7,72,8 East", "shape 9,72,8 West", "shape 8,72,7 South", "shape 8,72,9 North",
            "shape 8,71,8 Up", "shape 8,73,8 Down"
        ];
        Assert.Equal(expected, calls);

        // Without neighbour updates, the neighbours only get shape updates; with a known shape, not even those.
        calls.Clear();
        await world.SetBlockAsync(center, BlocksRegistry.Dirt, BlockUpdateFlags.Clients);
        Assert.Equal(6, calls.Count);
        Assert.All(calls, call => Assert.StartsWith("shape", call));

        calls.Clear();
        var knownShape = BlockUpdateFlags.Clients | BlockUpdateFlags.KnownShape;
        await world.SetBlockAsync(center, BlocksRegistry.Stone, knownShape);
        Assert.Empty(calls);

        // Placed, the relay schedules its tick two game ticks later; replaced, it's told what replaced it.
        await world.SetBlockAsync(center, relay.DefaultState, BlockUpdateFlags.All);
        Assert.Equal("placed 8,72,8 over stone", calls[0]);

        await world.DoWorldTickAsync();
        Assert.DoesNotContain("tick 8,72,8", calls);
        await world.DoWorldTickAsync();
        Assert.Contains("tick 8,72,8", calls);

        calls.Clear();
        await world.SetBlockAsync(center, BlocksRegistry.Air, BlockUpdateFlags.All);
        Assert.Equal("removed 8,72,8 for air", calls[0]);

        // Its shape update can change it: a relay turns to face a gold block placed next to it.
        var gold = BlocksRegistry.GetDefault(BlockIds.GoldBlock);
        await world.SetBlockAsync(center + Vector.East + Vector.East, gold, BlockUpdateFlags.All);
        var turned = chunk.GetBlock(center + Vector.East);
        Assert.Equal("east", relay.GetValue(turned, "facing"));

        // An entity inside a relay, whose entity hook throws: the failure is logged and the level's tick goes on.
        var region = world.GetRegionForChunk(0, 0)!;
        var crystal = new EndCrystal
        {
            EntityId = Obsidian.Server.GetNextEntityId(),
            Level = world,
            Position = new VectorD(center.X + 1.5, center.Y + 0.1, center.Z + 0.5)
        };

        region.Entities.TryAdd(crystal.EntityId, crystal);
        calls.Clear();
        await world.DoWorldTickAsync();
        region.Entities.TryRemove(crystal.EntityId, out _);

        Assert.Contains("inside 9,72,8", calls);
        Assert.Equal("idle", world.TickStage);

        // Relays take part in redstone: one facing south powers a lamp beside it, and a comparator reads its analog
        // output (7) into dust; another hears a lever beside it pulled, and reads its power.
        var southRelay = relay.With(relay.DefaultState, "facing", "south");
        var source = new Vector(2, Ground + 1, 12);
        var lamp = BlocksRegistry.GetDefault(BlockIds.RedstoneLamp);
        var comparator = BlocksRegistry.GetDefault(BlockIds.Comparator).WithProperty("facing", "west");
        await world.SetBlockAsync(source + Vector.West, lamp, BlockUpdateFlags.All);
        await world.SetBlockAsync(source + Vector.East, comparator, BlockUpdateFlags.All);
        await world.SetBlockAsync(source + Vector.East * 2, BlocksRegistry.RedstoneWire, BlockUpdateFlags.All);
        await world.SetBlockAsync(source, southRelay, BlockUpdateFlags.All);

        var lever = new Vector(8, Ground + 1, 12);
        var listener = lever + Vector.East;
        await world.SetBlockAsync(lever, BlocksRegistry.GetDefault(BlockIds.Lever), BlockUpdateFlags.All);
        await world.SetBlockAsync(listener, relay.DefaultState, BlockUpdateFlags.All);
        Assert.True(world.BlockUpdates.UseRedstone(lever, new FakePlayer(), mayBuild: true));

        await world.DoWorldTickAsync();
        await world.DoWorldTickAsync();

        Assert.Equal("true", chunk.GetBlock(source + Vector.West).GetProperty("lit"));
        Assert.Equal("7", chunk.GetBlock(source + Vector.East * 2).GetProperty("power"));
        Assert.Equal(15, world.GetSignal(source, BlockFace.East));
        Assert.Contains("power 9,70,12 15", calls);

        // Changes made at once each run their updates before the next change: the relay is placed, then removed.
        await world.SetBlockAsync(center, BlocksRegistry.Stone, BlockUpdateFlags.All);
        calls.Clear();

        var placing = Task.Run(async () =>
        {
            for (var change = 0; change < 200; change++)
                await world.SetBlockAsync(center, relay.DefaultState, BlockUpdateFlags.All);
        });

        var removing = Task.Run(async () =>
        {
            for (var change = 0; change < 200; change++)
                await world.SetBlockAsync(center, BlocksRegistry.Stone, BlockUpdateFlags.All);
        });

        await Task.WhenAll(placing, removing);

        var changes = calls.Where(call => call.StartsWith("placed") || call.StartsWith("removed")).ToList();
        Assert.NotEmpty(changes);
        for (var index = 0; index < changes.Count; index++)
        {
            var alternating = index % 2 == 0 ? "placed 8,72,8 over stone" : "removed 8,72,8 for stone";
            Assert.Equal(alternating, changes[index]);
        }

        // While the plugin unloads, with its blocks still bound to it, a relay's due tick waits rather than being
        // dropped; unloaded, the plugin's blocks stay but their hooks don't run.
        var waiting = new Vector(5, Ground + 1, 5);
        chunk.SetBlock(waiting, relay.DefaultState);
        world.ScheduleBlockTick(waiting, relay.DefaultState, delay: 1);
        container.Close();
        calls.Clear();
        world.Fluids.Tick();

        Assert.Empty(calls);
        Assert.True(((Chunk)chunk).BlockTicks.Contains(waiting, relay.DefaultState.UnlocalizedName));

        await manager.UnloadPluginAsync(container);
        await world.SetBlockAsync(center, BlocksRegistry.Air, BlockUpdateFlags.All);
        Assert.Empty(calls);
        Assert.Equal(0, world.GetSignal(source, BlockFace.East));

        // The circuits around the relay heard of its power and analog output going: the lamp goes out four ticks
        // later, and the comparator stops powering the dust.
        for (var tick = 0; tick < 4; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal("false", chunk.GetBlock(source + Vector.West).GetProperty("lit"));
        Assert.Equal("0", chunk.GetBlock(source + Vector.East * 2).GetProperty("power"));

        // Sand placed over air waits vanilla's two ticks before it falls, and lands with updates.
        var sand = new Vector(4, Ground + 4, 4);
        await world.SetBlockAsync(sand, BlocksRegistry.Sand, doBlockUpdate: true);

        await world.DoWorldTickAsync();
        Assert.True(chunk.GetBlock(sand).Is(BlockIds.Sand));

        await world.DoWorldTickAsync();
        Assert.True(chunk.GetBlock(sand).IsAir);

        for (var tick = 0; tick < 40 && !chunk.GetBlock(sand.X, Ground + 1, sand.Z).Is(BlockIds.Sand); tick++)
            await world.DoWorldTickAsync();

        Assert.True(chunk.GetBlock(sand.X, Ground + 1, sand.Z).Is(BlockIds.Sand));
    }

    [Fact(DisplayName = "Redstone circuits power, delay, lock, burn out and drop like vanilla's",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task RedstoneCircuitsMatchVanilla()
    {
        await using var context = TestServerFactory.CreateServer($"redstone-{Guid.NewGuid():N}");
        var world = context.World;
        await AddChunksAsync(world);
        world.LevelData.GameRules["spawn_mobs"] = "false";

        // A player near the ticking chunk, so that its containers tick and tell comparators of their items.
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server,
            world,
            "Redstoner",
            new VectorD(8, Ground + 1, 8));

        Assert.True(world.TryAddPlayer(player));

        IBlock Block(string name, params (string Name, string Value)[] properties) =>
            BlockStateProperties.GetState($"minecraft:{name}", properties.ToDictionary(p => p.Name, p => p.Value));

        Vector At(int x, int z, int up = 1) => new(x, Ground + up, z);

        var lever = Block("lever", ("face", "floor"), ("facing", "north"));
        var dust = Block("redstone_wire");
        var lamp = Block("redstone_lamp");
        var stone = Block("stone");
        var hopper = Block("hopper");
        IBlock Repeater(string facing, int delay) => Block("repeater", ("facing", facing), ("delay", $"{delay}"));
        IBlock Comparator(string facing, string mode) => Block("comparator", ("facing", facing), ("mode", mode));
        var torch = Block("redstone_wall_torch", ("facing", "east"));

        // Each circuit is built in the ticking chunk in the order listed, with block updates; the steps run after
        // that many game ticks, and the checks after the steps of their tick. Outcomes and timings are vanilla's,
        // checked by hand against 1.21.11: dust loses one power a block, repeaters wait two game ticks a delay,
        // torches two, comparators two, and lamps go out four game ticks after their power does.
        var build = new List<(Vector Position, IBlock State)>();
        var steps = new List<(int Tick, Func<ValueTask> Run)>();
        var checks = new List<(int Tick, Vector Position, string Property, string Value)>();
        void Use(int tick, Vector position) => steps.Add((tick, () =>
        {
            Assert.True(world.BlockUpdates.UseRedstone(position, player, mayBuild: true));
            return ValueTask.CompletedTask;
        }));

        // Dust carries a lever's power 15 blocks, into the lamp at its end; the lamp goes out four ticks after.
        build.Add((At(0, 0), lever));
        build.AddRange(Enumerable.Range(1, 14).Select(x => (At(x, 0), dust)));
        build.Add((At(15, 0), lamp));
        Use(0, At(0, 0));
        Use(3, At(0, 0));
        checks.AddRange(
        [
            (0, At(1, 0), "power", "15"), (0, At(14, 0), "power", "2"), (0, At(15, 0), "lit", "true"),
            (3, At(14, 0), "power", "0"), (6, At(15, 0), "lit", "true"), (7, At(15, 0), "lit", "false")
        ]);

        // Repeaters of delay 1 and 4 light a lamp 10 game ticks after the lever, and put it out 14 after.
        build.AddRange([(At(0, 2), lever), (At(1, 2), Repeater("west", 1)), (At(2, 2), Repeater("west", 4))]);
        build.Add((At(3, 2), lamp));
        Use(0, At(0, 2));
        Use(20, At(0, 2));
        checks.AddRange(
        [
            (1, At(1, 2), "powered", "false"), (2, At(1, 2), "powered", "true"), (9, At(3, 2), "lit", "false"),
            (10, At(3, 2), "lit", "true"), (33, At(3, 2), "lit", "true"), (34, At(3, 2), "lit", "false")
        ]);

        // A torch on a block a lever powers goes out two ticks after, and lights again two ticks after it's off.
        build.AddRange([(At(9, 2), stone), (At(9, 2, up: 2), lever), (At(11, 2), lamp), (At(10, 2), torch)]);
        Use(0, At(9, 2, up: 2));
        Use(10, At(9, 2, up: 2));
        checks.AddRange(
        [
            (0, At(11, 2), "lit", "true"), (1, At(10, 2), "lit", "true"), (2, At(10, 2), "lit", "false"),
            (5, At(11, 2), "lit", "true"), (6, At(11, 2), "lit", "false"), (12, At(11, 2), "lit", "true")
        ]);

        // A torch turned off 8 times within 60 game ticks burns out, and relights 160 ticks after the last.
        build.AddRange([(At(1, 4), stone), (At(1, 4, up: 2), lever), (At(2, 4), torch)]);
        for (var tick = 0; tick <= 45; tick += 3)
            Use(tick, At(1, 4, up: 2));

        checks.AddRange(
        [
            (5, At(2, 4), "lit", "true"), (44, At(2, 4), "lit", "false"), (47, At(2, 4), "lit", "false"),
            (203, At(2, 4), "lit", "false"), (204, At(2, 4), "lit", "true")
        ]);

        // A comparator reads a hopper holding a stack (3), and a block of redstone beside it beats that.
        build.AddRange([(At(9, 4), Comparator("west", "compare")), (At(10, 4), dust), (At(11, 4), dust)]);
        build.Add((At(8, 4), hopper));
        steps.Add((0, () => Fill(world, At(8, 4))));
        Use(6, At(9, 4));
        steps.Add((8, () => world.SetBlockAsync(At(9, 3), Block("redstone_block"), BlockUpdateFlags.All)));
        checks.AddRange(
        [
            (5, At(10, 4), "power", "3"), (5, At(11, 4), "power", "2"), (7, At(9, 4), "mode", "subtract"),
            (9, At(10, 4), "power", "3"), (10, At(10, 4), "power", "0")
        ]);

        // Subtracting, a comparator takes a side comparator's 3 from a lever's 15.
        build.AddRange([(At(2, 8), Comparator("west", "subtract")), (At(3, 8), dust), (At(4, 8), dust)]);
        build.AddRange([(At(2, 7), Comparator("north", "compare")), (At(2, 6), hopper), (At(1, 8), lever)]);
        steps.Add((0, () => Fill(world, At(2, 6))));
        Use(0, At(1, 8));
        checks.AddRange([(8, At(3, 8), "power", "12"), (8, At(4, 8), "power", "11")]);

        // Of two comparators in line due on the same tick, the one feeding the other ticks first, so the second reads
        // its new output: the first's hopper (3) becomes a block of redstone (15) as the second's side goes off.
        build.AddRange([(At(6, 10), hopper), (At(7, 10), Comparator("west", "compare"))]);
        build.AddRange([(At(8, 10), Comparator("west", "subtract")), (At(9, 10), dust)]);
        build.Add((At(8, 11), Block("redstone_block")));
        steps.Add((0, () => Fill(world, At(6, 10))));
        steps.Add((10, () => world.SetBlockAsync(At(8, 11), BlocksRegistry.Air, BlockUpdateFlags.All)));
        steps.Add((10, () => world.SetBlockAsync(At(6, 10), Block("redstone_block"), BlockUpdateFlags.All)));
        checks.AddRange([(9, At(9, 10), "power", "0"), (12, At(9, 10), "power", "15")]);

        // A repeater a powered repeater points into from the side holds its output until it's unlocked.
        build.AddRange([(At(0, 12), lever), (At(1, 12), Repeater("west", 1)), (At(2, 12), lamp)]);
        build.AddRange([(At(1, 10), lever), (At(1, 11), Repeater("north", 1))]);
        Use(0, At(0, 12));
        Use(5, At(1, 10));
        Use(10, At(0, 12));
        Use(20, At(1, 10));
        checks.AddRange(
        [
            (2, At(2, 12), "lit", "true"), (6, At(1, 12), "locked", "false"), (7, At(1, 12), "locked", "true"),
            (19, At(2, 12), "lit", "true"), (22, At(1, 12), "locked", "false"), (23, At(1, 12), "powered", "true"),
            (24, At(1, 12), "powered", "false"), (27, At(2, 12), "lit", "true"), (28, At(2, 12), "lit", "false")
        ]);

        // Dust whose block goes pops off, dropping its item.
        build.AddRange([(At(14, 14), stone), (At(14, 14, up: 2), dust)]);
        steps.Add((0, () => world.SetBlockAsync(At(14, 14), BlocksRegistry.Air, BlockUpdateFlags.All)));
        checks.Add((1, At(14, 14, up: 2), "block", "minecraft:air"));

        foreach (var (position, state) in build)
            await world.SetBlockAsync(position, state, BlockUpdateFlags.All);

        var chunk = world.GetLoadedChunk(0, 0)!;
        var last = checks.Max(check => check.Tick);
        for (var tick = 0; tick <= last; tick++)
        {
            foreach (var step in steps.Where(step => step.Tick == tick))
                await step.Run();

            foreach (var (_, position, property, value) in checks.Where(check => check.Tick == tick))
            {
                var state = chunk.GetBlock(position);
                var actual = property == "block" ? state.UnlocalizedName : state.GetProperty(property);
                Assert.True(value == actual, $"At tick {tick}, {property} at {position} is {actual}, not {value}.");
            }

            await world.DoWorldTickAsync();
        }

        // The dropped dust slides a little on the motion it popped out with.
        var drops = world.GetNonPlayerEntitiesInRange((VectorD)At(14, 14, up: 2), 8).OfType<ItemEntity>();
        Assert.Contains(drops, drop => drop.Item.Is(ItemsRegistry.Get("minecraft:redstone")));

        await AssertSupportLossAsync(world, player);
        world.TryRemovePlayer(player);
    }

    // Exercise the shared destruction boundary through ordinary shape updates, explicit shape flags and fluids.
    private static async Task AssertSupportLossAsync(WorldData.World world, Entities.Player player)
    {
        var chunk = world.GetLoadedChunk(0, 0)!;
        var basePosition = new Vector(3, Ground + 4, 7);
        var client = (TestClient)player.Client;
        player.LoadedChunks.Add(0);

        (string Block, string Item, Vector Offset, bool Effect)[] attachments =
        [
            ("torch", "torch", Vector.Up, true),
            ("wall_torch", "torch", Vector.East, true),
            ("dandelion", "dandelion", Vector.Up, true),
            ("white_carpet", "white_carpet", Vector.Up, true),
            ("oak_sign", "oak_sign", Vector.Up, true),
            ("white_banner", "white_banner", Vector.Up, true),
            ("oak_wall_sign", "oak_sign", Vector.East, true),
            ("white_wall_banner", "white_banner", Vector.East, true),
            ("oak_hanging_sign", "oak_hanging_sign", Vector.Down, true),
            ("oak_wall_hanging_sign", "oak_hanging_sign", Vector.North, true),
            ("lever", "lever", Vector.Up, true),
            ("stone_button", "stone_button", Vector.Up, true),
            ("redstone_torch", "redstone_torch", Vector.Up, true),
            ("stone_pressure_plate", "stone_pressure_plate", Vector.Up, true),
            ("rail", "rail", Vector.Up, false),
            ("redstone_wire", "redstone", Vector.Up, false)
        ];

        foreach (var (name, item, offset, effect) in attachments)
        {
            var position = basePosition + offset;
            var block = BlockStateProperties.GetState($"minecraft:{name}");
            if (offset == Vector.East)
                block = block.WithProperty("facing", "east");

            if (name == "oak_wall_hanging_sign")
                block = block.WithProperty("facing", "east");

            if (block.HasProperty("face"))
                block = block.WithProperty("face", "floor");

            chunk.SetBlock(basePosition, BlocksRegistry.Dirt);
            chunk.SetBlock(position, block);
            client.ClearPackets();
            await world.SetBlockAsync(basePosition, BlocksRegistry.Air, BlockUpdateFlags.All);
            Assert.True(chunk.GetBlock(position).IsSameState(block));
            await world.DoWorldTickAsync();
            Assert.True(chunk.GetBlock(position).IsAir, name);
            var dropped = Assert.Single(world.GetNonPlayerEntitiesInRange((VectorD)position, 2).OfType<ItemEntity>());
            Assert.Equal($"minecraft:{item}", dropped.Item.Holder.UnlocalizedName);
            Assert.Equal(1, dropped.Item.Count);
            var effects = client.SentPackets.OfType<LevelEventPacket>().Where(packet => packet.Position == position);
            Assert.Equal(effect ? [2001] : Array.Empty<int>(), effects.Select(packet => packet.Type));
            if (effect)
                Assert.Equal(block.GetHashCode(), Assert.Single(effects).Data);

            await dropped.RemoveAsync();
        }

        // SuppressDrops belongs to the shape update itself; Level.setBlock clears it for outgoing updates.
        var torchPosition = basePosition + Vector.Up;
        (IBlock Block, bool Suppress, bool Rule)[] suppression =
        [
            (BlocksRegistry.Torch, true, true),
            (BlocksRegistry.RedstoneTorch, true, true),
            (BlocksRegistry.Torch, false, false)
        ];
        foreach (var (block, suppress, rule) in suppression)
        {
            world.LevelData.GameRules["block_drops"] = rule ? "true" : "false";
            chunk.SetBlock(torchPosition, block);
            world.BlockUpdates.ShapeUpdateAt(torchPosition, BlockFace.Down,
                BlockUpdateFlags.Clients | (suppress ? BlockUpdateFlags.SuppressDrops : BlockUpdateFlags.None), 511);
            client.ClearPackets();
            await world.DoWorldTickAsync();
            Assert.True(chunk.GetBlock(torchPosition).IsAir);
            Assert.Empty(world.GetNonPlayerEntitiesInRange((VectorD)torchPosition, 2).OfType<ItemEntity>());
            Assert.Contains(client.SentPackets.OfType<LevelEventPacket>(), packet =>
                packet.Type == 2001 && packet.Position == torchPosition);
        }

        world.LevelData.GameRules["block_drops"] = "true";

        // Water's beforeDestroyingBlock drops the washed block but has no 2001; lava only fizzes (1501).
        (string Block, bool Water, bool Rule)[] washouts =
        [
            ("torch", true, true),
            ("wheat", true, true),
            ("torch", true, false),
            ("torch", false, true)
        ];
        var washPosition = basePosition;
        foreach (var (name, water, rule) in washouts)
        {
            world.LevelData.GameRules["block_drops"] = rule ? "true" : "false";
            var block = BlockStateProperties.GetState($"minecraft:{name}");
            if (name == "wheat")
                block = block.WithProperty("age", "7");

            var sourcePosition = washPosition + Vector.Up;
            chunk.SetBlock(washPosition, block);
            chunk.SetBlock(sourcePosition, water ? BlocksRegistry.Water : BlocksRegistry.Lava);
            client.ClearPackets();
            world.Fluids.ScheduleFluidTick(sourcePosition, water ? FluidKind.Water : FluidKind.Lava, 0);
            await world.DoWorldTickAsync();
            Assert.True(chunk.GetBlock(washPosition).IsLiquid, $"{name}, water={water}, drops={rule}");
            var washed = world.GetNonPlayerEntitiesInRange((VectorD)washPosition, 2).OfType<ItemEntity>().ToArray();
            if (water && rule)
            {
                var expected = ItemsRegistry.Get($"minecraft:{name}");
                Assert.Equal(1, Assert.Single(washed, drop => drop.Item.Is(expected)).Item.Count);
                Assert.All(washed, drop => Assert.True(drop.Item.Is(expected)
                    || name == "wheat" && drop.Item.Is(ItemsRegistry.WheatSeeds)));
                foreach (var drop in washed)
                    await drop.RemoveAsync();
            }
            else
            {
                Assert.Empty(washed);
            }

            Assert.DoesNotContain(client.SentPackets.OfType<LevelEventPacket>(), packet =>
                packet.Type == 2001 && packet.Position == washPosition);
            if (!water)
                Assert.Contains(client.SentPackets.OfType<LevelEventPacket>(), packet =>
                    packet.Type == 1501 && packet.Position == washPosition);

            chunk.SetBlock(washPosition, BlocksRegistry.Air);
            chunk.SetBlock(sourcePosition, BlocksRegistry.Air);
            washPosition += Vector.East * 3;
        }
    }

    // Fills a hopper's first slot with a stack of stone, as a player moving items in would.
    private static async ValueTask Fill(WorldData.World world, Vector position)
    {
        var hopper = (API.BlockEntities.ContainerBlockEntity)(await world.GetBlockEntityAsync(position))!;
        hopper.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Get("minecraft:stone"), 64));
    }

    // A player using their main hand on the face of the block at position, at the middle of its top.
    internal static UseItemOnPacket UseOn(Vector position, BlockFace face)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarInt((int)InteractionHand.MainHand);
        writer.WritePosition(position);
        writer.WriteVarInt((int)face);
        writer.WriteAbsoluteFloatPositionF(new VectorF(0.5f, 1, 0.5f));
        writer.WriteBoolean(false);
        writer.WriteBoolean(false);
        writer.WriteVarInt(1);

        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new UseItemOnPacket();
        packet.Populate(reader);

        return packet;
    }

    // The section above the ground in the middle chunk, every block set to block.
    private static void FillSection(WorldData.World world, IBlock block)
    {
        var chunk = world.GetLoadedChunk(0, 0)!;
        for (var y = 80; y < 96; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    chunk.SetBlock(x, y, z, block);
            }
        }
    }

    internal static PluginManager CreatePluginManager(RealServerContext context) => new(
        context.Server,
        context.Provider,
        context.EventDispatcher,
        context.Provider.GetRequiredService<CommandHandler>(),
        NullLogger<PluginManager>.Instance,
        new ConfigurationBuilder().Build());

    private async Task<PluginContainer> LoadProbeAsync(PluginManager manager)
    {
        var path = Path.Join(this.folder, "probe.obby");
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));

        return (await manager.LoadPluginAsync(path))!;
    }

    // Complete chunks around the origin with a stone floor, loaded as the level loads them, so they tick.
    internal static async Task AddChunksAsync(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ);
                for (var x = 0; x < 16; x++)
                {
                    for (var z = 0; z < 16; z++)
                        chunk.SetBlock(chunkX * 16 + x, Ground, chunkZ * 16 + z, BlocksRegistry.Stone);
                }

                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
            }
        }

        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
        }
    }
}

/// <summary>
/// The probe plugin's blocks for <see cref="BlockUpdates"/>: <c>probe:relay</c> records each hook it gets in
/// <see cref="AppContext"/> data (the plugin's statics are its own copy's), faces away from the player placing it,
/// turns towards a gold block placed east or west of it, gets random ticks and throws from its entity hook. Facing
/// south, it gives full redstone power and an analog output of 7; it records the power it gets when a neighbour
/// changes.
/// </summary>
public sealed class ProbeRelay : IBlockBehavior
{
    public const string CallsKey = "Obsidian.Tests.ProbeRelay.Calls";

    private BlockType? type;

    public static void Register(IPluginRegistry registry)
    {
        var relay = new ProbeRelay();
        relay.type = registry.RegisterBlock("probe:relay", new BlockProperties
        {
            StateProperties = [StateProperty.Enum("facing", "north", "south", "west", "east")],
            RandomlyTicks = true,
            Behavior = relay
        });
    }

    /// <summary>
    /// Starts recording afresh.
    /// </summary>
    /// <returns>The calls the relays record from now on.</returns>
    public static List<string> Reset()
    {
        var calls = new List<string>();
        AppContext.SetData(CallsKey, calls);
        return calls;
    }

    public IBlock? GetStateForPlacement(BlockPlaceContext context) =>
        this.type!.With(context.Block, "facing", Name(context.HorizontalDirection.Opposite()));

    public void OnPlaced(BlockPlacedContext context)
    {
        Record($"placed {At(context.Position)} over {Short(context.OldState)}");
        context.Level.ScheduleTick(context.Position, context.State, 2);
    }

    public void OnRemoved(BlockRemovedContext context) =>
        Record($"removed {At(context.Position)} for {Short(context.NewState)}");

    public void NeighborChanged(NeighborChangedContext context)
    {
        Record($"neighbor {At(context.Position)} from {Short(context.SourceBlock)}");

        var power = context.Level.GetBestNeighborSignal(context.Position);
        if (power > 0)
            Record($"power {At(context.Position)} {power}");
    }

    public bool IsSignalSource(IBlock state) => true;

    public int GetSignal(SignalContext context) => this.FacesSouth(context.State) ? 15 : 0;

    public bool HasAnalogOutput(IBlock state) => true;

    public int GetAnalogOutput(SignalContext context) => this.FacesSouth(context.State) ? 7 : 0;

    public IBlock UpdateShape(ShapeUpdateContext context)
    {
        Record($"shape {At(context.Position)} {context.Direction}");

        var turns = context.NeighborState.Is(BlockIds.GoldBlock)
            && context.Direction is BlockFace.East or BlockFace.West;

        return turns
            ? this.type!.With(context.State, "facing", Name(context.Direction))
            : context.State;
    }

    public void Tick(BlockContext context) => Record($"tick {At(context.Position)}");

    public void RandomTick(BlockContext context) => Record($"random {At(context.Position)}");

    // Entity hooks throw after recording, as a broken plugin's might.
    public ValueTask EntityInsideAsync(EntityBlockContext context)
    {
        Record($"inside {At(context.Position)}");
        throw new InvalidOperationException("The relay's entity hook failed.");
    }

    private bool FacesSouth(IBlock state) => this.type!.GetValue(state, "facing") == "south";

    private static void Record(string call)
    {
        if (AppContext.GetData(CallsKey) is List<string> calls)
            calls.Add(call);
    }

    private static string At(Vector position) => $"{position.X},{position.Y},{position.Z}";

    private static string Short(IBlock block) => block.UnlocalizedName.Replace("minecraft:", "");

    private static string Name(BlockFace face) => face.ToString().ToLowerInvariant();
}
