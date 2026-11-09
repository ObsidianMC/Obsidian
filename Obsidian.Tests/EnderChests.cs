using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Ender chests open the player's own ender inventory, so what goes in stays with them: not lost when the window
/// closes, not copied, and not shown to other players.
/// </summary>
public sealed class EnderChests
{
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    [Fact(DisplayName = "Ender chests keep the player's items from window to window and chest to chest, for them only")]
    public async Task EnderInventoryFollowsThePlayer()
    {
        await using var context = TestServerFactory.CreateServer($"ender-chests-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(Vector.Zero);

        var first = new Vector(4, 70, 4);
        var second = new Vector(8, 70, 4);
        world.BlockUpdates.SetBlock(first, BlocksRegistry.EnderChest, Quiet);
        world.BlockUpdates.SetBlock(second, BlocksRegistry.EnderChest, Quiet);

        var owner = Player(context, "Owner");
        var other = Player(context, "Other");

        // Items put in through one chest...
        var window = await OpenAsync(context, owner, first);
        Assert.Same(owner.EnderInventory, window);
        window.SetItem(0, new ItemStack(ItemsRegistry.Get("minecraft:diamond"), 3));
        await CloseAsync(context, owner);

        // ...are there, once, through the other.
        var reopened = await OpenAsync(context, owner, second);
        Assert.Equal(3, Diamonds(reopened));
        Assert.Equal(0, Diamonds(owner.Inventory));
        await CloseAsync(context, owner);

        // Another player's ender chest is their own.
        var others = await OpenAsync(context, other, first);
        Assert.Equal(0, Diamonds(others));
        Assert.Equal(3, Diamonds(owner.EnderInventory));
    }

    private static ServerPlayer Player(RealServerContext context, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name,
            new VectorD(6.5, 70, 6.5));
        player.GameMode = GameMode.Survival;
        context.World.Players.TryAdd(player.Uuid, player);

        return player;
    }

    private static async Task<BaseContainer> OpenAsync(RealServerContext context, ServerPlayer player, Vector chest)
    {
        var use = BlockUpdates.UseOn(chest, BlockFace.South, new VectorF(0.5f, 0.5f, 0.5f));
        await use.HandleAsync(context.Server, player);

        return Assert.IsAssignableFrom<BaseContainer>(player.OpenedContainer);
    }

    private static async Task CloseAsync(RealServerContext context, ServerPlayer player)
    {
        var closed = new ContainerClosedEventArgs(player, context.Server, player.OpenedContainer!,
            player.CurrentContainerId);
        await context.EventDispatcher.ExecuteEventAsync(closed);
        Assert.Null(player.OpenedContainer);
    }

    private static int Diamonds(BaseContainer container) =>
        Enumerable.Range(0, container.Size)
            .Select(container.GetItem)
            .Where(stack => stack?.Holder.UnlocalizedName == "minecraft:diamond")
            .Sum(stack => stack!.Count);
}
