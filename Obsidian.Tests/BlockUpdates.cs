using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Plugins;
using Obsidian.API.Registries;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net;
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
    private const int Ground = 69;

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-block-updates-{Guid.NewGuid():N}");

    public BlockUpdates() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    [Fact(DisplayName = "Block ticks save as vanilla's block_ticks and run by time, priority and scheduling order")]
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
        for (var tick = 0; tick < 3; tick++)
            scheduler.Tick(_ => true, (position, _) => ran.Add(position.X));

        // Game tick 1: the one due then. Game tick 2: by priority, then the order they were scheduled in.
        Assert.Equal([2, 3, 1, 4, 5], ran);
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
        context.EventDispatcher.RegisterEvent(null, (Func<BlockPlaceEventArgs, ValueTask>)(placing =>
        {
            events++;
            Assert.Same(player, placing.Player);
            if (cancel)
                placing.Cancel();

            return ValueTask.CompletedTask;
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
        var chunk = world.GetLoadedChunk(0, 0)!;
        var center = new Vector(8, Ground + 3, 8);
        Vector[] around = [Vector.Up, Vector.Down, Vector.North, Vector.South, Vector.West, Vector.East];
        foreach (var offset in around)
            chunk.SetBlock(center + offset, relay.DefaultState);

        var calls = ProbeRelay.Reset();
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

        // Unloaded, the plugin's blocks stay but their hooks don't run.
        await manager.UnloadPluginAsync(container);
        calls.Clear();
        await world.SetBlockAsync(center, BlocksRegistry.Stone, BlockUpdateFlags.All);
        Assert.Empty(calls);
    }

    [Fact(DisplayName = "Random ticks reach plugin blocks that opt in; falling blocks fall on their scheduled tick",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task RandomTicksAndFallingBlocks()
    {
        await using var context = TestServerFactory.CreateServer($"block-ticks-{Guid.NewGuid():N}");
        var world = context.World;
        await AddChunksAsync(world);
        await using var manager = CreatePluginManager(context);
        var container = await this.LoadProbeAsync(manager);

        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:relay", out var relay));
        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:still", out var still));

        var player = new FakePlayer { Position = new VectorD(8, Ground + 1, 8) };
        Assert.True(world.TryAddPlayer(player));

        try
        {
            // A section of relays gets the game rule's number of random ticks; one of blocks that don't opt in, none.
            var calls = ProbeRelay.Reset();
            FillSection(world, still.DefaultState);
            world.TickRandomBlocks();
            Assert.Empty(calls);

            FillSection(world, relay.DefaultState);
            world.TickRandomBlocks();
            Assert.Equal(world.LevelData.GetIntegerRule("random_tick_speed", 3), calls.Count);
            Assert.All(calls, call => Assert.StartsWith("random", call));
            FillSection(world, BlocksRegistry.Air);
        }
        finally
        {
            world.TryRemovePlayer(player);
        }

        await manager.UnloadPluginAsync(container);

        // Sand placed over air waits vanilla's two ticks before it falls, and lands with updates.
        var sand = new Vector(4, Ground + 4, 4);
        var chunk = world.GetLoadedChunk(0, 0)!;
        await world.SetBlockAsync(sand, BlocksRegistry.Sand, doBlockUpdate: true);

        await world.DoWorldTickAsync();
        Assert.True(chunk.GetBlock(sand).Is(BlockIds.Sand));

        await world.DoWorldTickAsync();
        Assert.True(chunk.GetBlock(sand).IsAir);

        for (var tick = 0; tick < 40 && !chunk.GetBlock(sand.X, Ground + 1, sand.Z).Is(BlockIds.Sand); tick++)
            await world.DoWorldTickAsync();

        Assert.True(chunk.GetBlock(sand.X, Ground + 1, sand.Z).Is(BlockIds.Sand));
    }

    private static UseItemOnPacket UseOn(Vector position, BlockFace face)
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

    private static PluginManager CreatePluginManager(RealServerContext context) => new(
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
    private static async Task AddChunksAsync(WorldData.World world)
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
/// turns towards a gold block placed east or west of it, and gets random ticks; <c>probe:still</c> records the same,
/// without random ticks.
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

        var still = new ProbeRelay();
        still.type = registry.RegisterBlock("probe:still", new BlockProperties { Behavior = still });
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

    public void NeighborChanged(NeighborChangedContext context) =>
        Record($"neighbor {At(context.Position)} from {Short(context.SourceBlock)}");

    public IBlock UpdateShape(ShapeUpdateContext context)
    {
        Record($"shape {At(context.Position)} {context.Direction}");

        var turns = context.NeighborState.Is(BlockIds.GoldBlock)
            && context.Direction is BlockFace.East or BlockFace.West;

        return turns && this.type!.Properties.Length > 0
            ? this.type.With(context.State, "facing", Name(context.Direction))
            : context.State;
    }

    public void Tick(BlockContext context) => Record($"tick {At(context.Position)}");

    public void RandomTick(BlockContext context) => Record($"random {At(context.Position)}");

    private static void Record(string call)
    {
        if (AppContext.GetData(CallsKey) is List<string> calls)
            calls.Add(call);
    }

    private static string At(Vector position) => $"{position.X},{position.Y},{position.Z}";

    private static string Short(IBlock block) => block.UnlocalizedName.Replace("minecraft:", "");

    private static string Name(BlockFace face) => face.ToString().ToLowerInvariant();
}
