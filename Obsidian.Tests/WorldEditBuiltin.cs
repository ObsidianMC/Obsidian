using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.BlockEntities;
using Obsidian.API.Configuration;
using Obsidian.API.Events;
using Obsidian.API.Commands;
using Obsidian.API.Plugins;
using Obsidian.API.World;
using Obsidian.API.World.Generator;
using Obsidian.API.Utilities;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Builtins.WorldEdit;
using Obsidian.Builtins;
using Obsidian.Commands.Framework;
using Obsidian.Plugins;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class WorldEditBuiltin
{
    [Fact]
    public void BuiltinsAreOptInAndCompiledIntoObsidian()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Empty(new ServerConfiguration().Builtins);
        Assert.Empty(Builtin.Enabled(configuration));
        configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Builtins:worldedit"] = "true"
        }).Build();
        var builtin = Assert.Single(Builtin.Enabled(configuration));
        Assert.Equal("worldedit", builtin.Metadata.Id);
        Assert.Same(typeof(Server).Assembly, builtin.GetType().Assembly);
        Assert.Empty(builtin.Resources().Paths);
    }

    [Fact]
    public void OnlyMarkedAxesAreWands()
    {
        Assert.False(WorldEditTools.IsWand(null));
        Assert.False(WorldEditTools.IsWand(new ItemStack(ItemsRegistry.WoodenAxe)));
        var wand = WorldEditTools.CreateWand();
        Assert.True(WorldEditTools.IsWand(wand));
        Assert.True(WorldEditTools.IsWand(new ItemStack(wand)));
        Assert.True(WorldEditTools.IsWand(wand.ToNbt().ItemFromNbt()));
        Assert.True(wand.ContainsKey(DataComponentType.CustomData));
        wand.Remove(DataComponentType.CustomData);
        Assert.False(WorldEditTools.IsWand(wand));
    }

    [Fact]
    public void RegionsNormalizeAndCountInclusiveBoundsWithoutOverflow()
    {
        var region = new EditRegion(new Vector(3, 2, 1), new Vector(1, 0, -1));
        Assert.Equal(new Vector(1, 0, -1), region.Min);
        Assert.Equal(new Vector(3, 2, 1), region.Max);
        Assert.Equal(27, region.Volume);
        Assert.Equal(27, region.Positions().Distinct().Count());
        Assert.Throws<ArgumentException>(() => new EditRegion(new Vector(int.MinValue), new Vector(int.MaxValue)).CheckLimit());
    }

    [Fact]
    public void ClipboardCoordinatesRotateAroundTheCopyOrigin()
    {
        Assert.Equal(new Vector(-3, 2, 1), BlockTransforms.RotatePosition(new Vector(1, 2, 3), 90));
        Assert.Equal(new Vector(1, 2, 3), BlockTransforms.RotatePosition(new Vector(1, 2, 3), 360));
        Assert.Equal(new Vector(1, 2, -3), BlockTransforms.FlipPosition(new Vector(1, 2, 3), "z"));
        Assert.Throws<ArgumentException>(() => BlockTransforms.RotatePosition(new Vector(1), 45));
    }

    [Fact]
    public async Task ClipboardMoveAndHistoryKeepOverlappingSourceBlocks()
    {
        await using var context = TestServerFactory.CreateServer($"worldedit-history-{Guid.NewGuid():N}");
        var level = context.World;
        await BlockUpdates.AddChunksAsync(level);
        var p = new Vector(2, BlockUpdates.Ground + 1, 2);
        await level.SetBlockAsync(p, BlockPattern.Resolve("stone"));
        await level.SetBlockAsync(p + new Vector(1, 0, 0), BlockPattern.Resolve("dirt"));
        var session = new EditSession();
        session.Select(level, p, first: true);
        session.Select(level, p + new Vector(1, 0, 0), first: false);
        Assert.Equal(2, session.Copy(level, p));
        await session.MoveAsync(level, new Vector(1, 0, 0));
        Assert.True(level.CaptureBlock(p).Block.IsAir);
        Assert.Equal("minecraft:stone", level.CaptureBlock(p + new Vector(1, 0, 0)).Block.UnlocalizedName);
        Assert.Equal("minecraft:dirt", level.CaptureBlock(p + new Vector(2, 0, 0)).Block.UnlocalizedName);
        await session.ReplayAsync(redo: false);
        Assert.Equal("minecraft:stone", level.CaptureBlock(p).Block.UnlocalizedName);
        Assert.Equal("minecraft:dirt", level.CaptureBlock(p + new Vector(1, 0, 0)).Block.UnlocalizedName);
        Assert.True(level.CaptureBlock(p + new Vector(2, 0, 0)).Block.IsAir);
        await session.ReplayAsync(redo: true);
        Assert.True(level.CaptureBlock(p).Block.IsAir);
        await session.PasteAsync(level, p + new Vector(0, 0, 2), skipAir: false, select: true);
        Assert.Equal("minecraft:stone", level.CaptureBlock(p + new Vector(0, 0, 2)).Block.UnlocalizedName);
        Assert.Equal("minecraft:dirt", level.CaptureBlock(p + new Vector(1, 0, 2)).Block.UnlocalizedName);
        Assert.Empty(session.RedoHistory);
        Assert.Empty(new EditSession().UndoHistory);
    }

    [Fact]
    public async Task InvalidAreasFailBeforeTheFirstWrite()
    {
        await using var context = TestServerFactory.CreateServer($"worldedit-bounds-{Guid.NewGuid():N}");
        await BlockUpdates.AddChunksAsync(context.World);
        var p = new Vector(2, BlockUpdates.Ground + 1, 2);
        var session = new EditSession();
        var desired = new Dictionary<Vector, BlockSnapshot>
        {
            [p] = new(BlockPattern.Resolve("stone")),
            [new Vector(16000, p.Y, 16000)] = new(BlockPattern.Resolve("stone"))
        };
        await Assert.ThrowsAsync<ArgumentException>(() => session.ApplyAsync(context.World, desired));
        Assert.True(context.World.CaptureBlock(p).Block.IsAir);
        Assert.Empty(session.UndoHistory);
        Assert.Throws<ArgumentException>(() => BlockPattern.Resolve("definitely_not_a_block"));
        Assert.Throws<ArgumentException>(() => BlockPattern.Resolve("oak_stairs[facing=invalid]"));
    }

    [Fact]
    public async Task UndoAndCopyPreserveContainerDataWithoutDroppingContents()
    {
        await using var context = TestServerFactory.CreateServer($"worldedit-containers-{Guid.NewGuid():N}");
        await BlockUpdates.AddChunksAsync(context.World);
        var p = new Vector(2, BlockUpdates.Ground + 1, 2);
        var level = context.World;
        await level.SetBlockAsync(p, BlockPattern.Resolve("chest"));
        var chest = Assert.IsAssignableFrom<ContainerBlockEntity>(await level.GetBlockEntityAsync(p));
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 3));
        var session = new EditSession();
        session.Select(level, p, first: true);
        session.Select(level, p, first: false);
        session.Copy(level, p);
        await session.FillAsync(level, new BlockPattern("stone"), "set");
        await session.ReplayAsync(redo: false);
        var restored = Assert.IsAssignableFrom<ContainerBlockEntity>(await level.GetBlockEntityAsync(p));
        Assert.True(restored.Inventory.GetItem(0).Is(ItemsRegistry.Diamond));
        Assert.Equal(3, restored.Inventory.GetItem(0).Count);
        var target = p + new Vector(0, 0, 2);
        await session.PasteAsync(level, target, skipAir: false, select: false);
        var copy = Assert.IsAssignableFrom<ContainerBlockEntity>(await level.GetBlockEntityAsync(target));
        Assert.Equal(3, copy.Inventory.GetItem(0).Count);
        copy.Inventory.SetItem(0, null);
        Assert.Equal(3, restored.Inventory.GetItem(0).Count);
        Assert.DoesNotContain(level.GetEntitiesInRange(new VectorD(p.X, p.Y, p.Z), 32),
            entity => entity is Obsidian.Entities.ItemEntity);
    }

    [Fact]
    public async Task ClipboardTransformsDirectionalStatesAndValidatesPatterns()
    {
        await using var context = TestServerFactory.CreateServer($"worldedit-transform-{Guid.NewGuid():N}");
        var stairs = BlockPattern.Resolve("oak_stairs[facing=east,shape=inner_left]");
        var rotated = BlockTransforms.RotateBlock(stairs, 90).ToSimpleState();
        Assert.Equal("south", rotated.Properties["facing"]);
        var flipped = BlockTransforms.FlipBlock(stairs, "x").ToSimpleState();
        Assert.Equal("west", flipped.Properties["facing"]);
        Assert.Equal("inner_right", flipped.Properties["shape"]);
        var chest = BlockPattern.Resolve("chest[facing=north,type=left]");
        Assert.Equal("right", BlockTransforms.FlipBlock(chest, "x").ToSimpleState().Properties["type"]);
        Assert.Equal(new[] { "stone", "oak_stairs[facing=east,half=top]" },
            BlockPattern.Split("stone,oak_stairs[facing=east,half=top]"));
        Assert.Throws<ArgumentException>(() => new BlockPattern("0%stone"));
        Assert.Throws<ArgumentException>(() => new BlockPattern("stone,"));
    }

    [Fact]
    public async Task HostBuiltinUsesRegistrationLifecycleAndOnlyInterceptsMarkedOperatorTools()
    {
        await using var context = TestServerFactory.CreateServer($"worldedit-builtin-{Guid.NewGuid():N}");
        await BlockUpdates.AddChunksAsync(context.World);
        context.Server.CommandHandler.RegisterCommands();
        Assert.DoesNotContain(context.Server.CommandHandler.GetAllCommands(), command => command.Name == "/wand");

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Builtins:worldedit"] = "true"
        }).Build();
        await using var manager = new PluginManager(context.Server, context.Provider, context.EventDispatcher,
            (CommandHandler)context.Server.CommandHandler, NullLogger<PluginManager>.Instance, configuration);
        await manager.LoadBuiltinsAsync();
        var builtin = Assert.Single(manager.Plugins);
        Assert.True(builtin.Loaded);
        Assert.Same(typeof(Server).Assembly, builtin.PluginAssembly);
        Assert.Null(builtin.LoadContext);
        var commands = context.Server.CommandHandler.GetAllCommands();
        Assert.Single(commands, command => command.Name == "/wand");
        Assert.All(commands.Where(command => command.PluginContainer == builtin),
            command => Assert.True(command.Name.StartsWith('/') || command.Name == "worldedit"));
        await manager.LoadBuiltinsAsync();
        Assert.Single(manager.Plugins);
        Assert.Single(context.Server.CommandHandler.GetAllCommands(), command => command.Name == "/wand");

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Editor");
        var p = new Vector(2, BlockUpdates.Ground, 2);
        var block = context.World.CaptureBlock(p).Block;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, WorldEditTools.CreateWand());
        var denied = new BlockAttackEventArgs(context.Server, player, block, p, context.World);
        await context.EventDispatcher.ExecuteEventAsync(denied);
        Assert.False(denied.IsCancelled);
        context.Server.Operators.AddOperator(player);
        var marked = new BlockAttackEventArgs(context.Server, player, block, p, context.World);
        await context.EventDispatcher.ExecuteEventAsync(marked);
        Assert.True(marked.IsCancelled);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.WoodenAxe));
        var normal = new BlockAttackEventArgs(context.Server, player, block, p, context.World);
        await context.EventDispatcher.ExecuteEventAsync(normal);
        Assert.False(normal.IsCancelled);
        await context.Server.CommandHandler.ProcessCommand(new CommandContext("//wand",
            new CommandSender(CommandIssuers.Client, player), player, context.Server));
        Assert.False(WorldEditTools.IsWand(player.GetHeldItem()));
        Assert.True(Enumerable.Range(9, 36).Any(slot => WorldEditTools.IsWand(player.Inventory.GetItem(slot))));
        Assert.Contains(((TestClient)player.Client).SentPackets.OfType<ContainerSetSlotPacket>(),
            packet => WorldEditTools.IsWand(packet.SlotData));
        await manager.UnloadPluginAsync(builtin);
        Assert.DoesNotContain(context.Server.CommandHandler.GetAllCommands(), command => command.Name == "/wand");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, WorldEditTools.CreateWand());
        var unloaded = new BlockAttackEventArgs(context.Server, player, block, p, context.World);
        await context.EventDispatcher.ExecuteEventAsync(unloaded);
        Assert.False(unloaded.IsCancelled);
    }
}
