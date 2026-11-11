using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Entities.AI;

namespace Obsidian.WorldData;

/// <summary>
/// A block whose use opens a menu: vanilla's blocks with a <c>MenuProvider</c>. Using one asks for the menu through
/// <see cref="PlayerInteractEventArgs"/>, which plugins may cancel, and the server's own handler opens it
/// (<see cref="BlockMenus.OpenAsync"/>).
/// </summary>
internal interface IBlockMenu
{
    /// <summary>Whether a spectator's use opens it, as vanilla opens a block's <c>getMenuProvider</c>.</summary>
    bool OpensForSpectators => true;

    /// <summary>The <c>minecraft:custom</c> statistic opening it awards, if any.</summary>
    string? Statistic(IBlock block) => null;

    /// <summary>
    /// The menu's container for <paramref name="player"/>, or null when it doesn't open. A block entity's own
    /// inventory replaces it afterwards (see <see cref="BlockMenus.OpenAsync"/>).
    /// </summary>
    ValueTask<BaseContainer?> CreateAsync(IPlayer player, AbstractLevel level, IBlock block, Vector position);
}

internal static partial class UseBehaviors
{
    /// <summary>
    /// A block that opens a menu when used, whatever the player holds: vanilla's container and workstation
    /// <c>useWithoutItem</c>.
    /// </summary>
    private class MenuUse(string? statistic, Func<IPlayer, Vector, BaseContainer>? create = null)
        : IBlockUseBehavior, IBlockMenu
    {
        public virtual bool OpensForSpectators => true;

        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            await BlockMenus.RequestAsync(use);
            return UseResult.Success;
        }

        public virtual string? Statistic(IBlock block) => statistic;

        public virtual ValueTask<BaseContainer?> CreateAsync(IPlayer player, AbstractLevel level, IBlock block,
            Vector position)
        {
            // Without a factory, the block entity's own inventory is the menu.
            var container = create is null ? new Container { BlockPosition = position } : create(player, position);
            return ValueTask.FromResult<BaseContainer?>(container);
        }
    }

    /// <summary>
    /// Vanilla's <c>ChestBlock</c>, for chests, trapped chests and copper chests: a chest next to its other half
    /// opens both as one.
    /// </summary>
    private sealed class ChestMenuUse() : MenuUse(statistic: null)
    {
        public override string? Statistic(IBlock block) =>
            block.Is(BlockIds.TrappedChest) ? "minecraft:trigger_trapped_chest" : "minecraft:open_chest";

        public override async ValueTask<BaseContainer?> CreateAsync(IPlayer player, AbstractLevel level, IBlock block,
            Vector position)
        {
            // Programmatic menu events may precede loading the chunk; obstruction checks read resident state.
            var current = await level.GetBlockAsync(position);
            if (current is null || !current.IsSameState(block))
                return null;

            if (BlockMenus.ChestBlocked(level, block, position))
                return null;

            var fresh = BlockMenus.NewChest(position);
            var container = await BlockMenus.StoredContainerAsync(player, level, position, fresh);
            var joined = await BlockMenus.DoubleChestAsync(player, level, block, position, container);
            return BlockMenus.ChestBlocked(level, block, position) ? null : joined;
        }
    }

    /// <summary>
    /// Vanilla's <c>EnderChestBlock</c>: the player's own ender inventory, which stays with them from chest to
    /// chest. A block that conducts redstone above it keeps it shut, and spectators don't open it.
    /// </summary>
    private sealed class EnderChestMenuUse() : MenuUse("minecraft:open_enderchest")
    {
        public override bool OpensForSpectators => false;

        public override async ValueTask<BaseContainer?> CreateAsync(IPlayer player, AbstractLevel level, IBlock block,
            Vector position)
        {
            var above = level.GameEventBlock(position + Vector.Up);
            if (above?.IsRedstoneConductor() == true || player is not PlayerBase owner)
                return null;

            // Like vanilla's PlayerEnderChestContainer.setActiveChest: the inventory is shown at the chest it was
            // opened from, whose lid opens while it's viewed.
            var inventory = owner.EnderInventory;
            if (ReferenceEquals(owner.OpenedContainer, inventory))
                await owner.CloseContainerAsync();

            inventory.Id = "ender_chest";
            inventory.BlockPosition = position;
            inventory.WindowBlockEntity = null;

            return inventory;
        }
    }

    /// <summary>
    /// Vanilla's <c>ShulkerBoxBlock</c>: a closed box opens only with room for its lid the way it faces (see
    /// <see cref="ShulkerLids.CanOpen"/>); a box that can't still counts as used.
    /// </summary>
    private sealed class ShulkerBoxMenuUse() : MenuUse("minecraft:open_shulker_box")
    {
        public override ValueTask<BaseContainer?> CreateAsync(IPlayer player, AbstractLevel level, IBlock block,
            Vector position)
        {
            bool canOpen;
            lock (level.Fluids.TickLock)
                canOpen = ShulkerLids.CanOpen(level, block, position);

            if (!canOpen)
                return ValueTask.FromResult<BaseContainer?>(null);

            return base.CreateAsync(player, level, block, position);
        }
    }

    /// <summary>Vanilla's <c>DispenserBlock</c>, for dispensers and droppers: a 3×3 menu.</summary>
    private sealed class DispenserMenuUse() : MenuUse(statistic: null)
    {
        public override string? Statistic(IBlock block) =>
            block.Is(BlockIds.Dropper) ? "minecraft:inspect_dropper" : "minecraft:inspect_dispenser";
    }

    /// <summary>Vanilla's <c>AbstractFurnaceBlock</c>, for furnaces, blast furnaces and smokers.</summary>
    private sealed class FurnaceMenuUse() : MenuUse(statistic: null)
    {
        public override string? Statistic(IBlock block) => block.RegistryId switch
        {
            BlockIds.BlastFurnace => "minecraft:interact_with_blast_furnace",
            BlockIds.Smoker => "minecraft:interact_with_smoker",
            _ => "minecraft:interact_with_furnace"
        };
    }
}

/// <summary>Opens the menus of blocks (<see cref="IBlockMenu"/>).</summary>
internal static class BlockMenus
{
    /// <summary>
    /// Asks for the menu of the used block, through the event plugins may cancel. The server's handler of the
    /// event opens it.
    /// </summary>
    internal static async ValueTask RequestAsync(BlockUse use) =>
        await use.Server.EventDispatcher.ExecuteEventAsync(new PlayerInteractEventArgs(use.Player, use.Server)
        {
            Item = use.Item,
            Block = use.Block,
            BlockLocation = use.Position,
            Hand = use.Hand
        });

    /// <summary>
    /// Opens the menu of <paramref name="block"/> at <paramref name="position"/> for <paramref name="player"/>, if it
    /// has one and it opens, and awards its statistic.
    /// </summary>
    internal static async ValueTask OpenAsync(IPlayer player, IBlock block, Vector position)
    {
        if (player.Level is not AbstractLevel level
            || player is PlayerBase { Alive: false } or PlayerBase { Respawning: true }
            || BlocksRegistry.GetUseWithoutItemBehavior(block.RegistryId) is not IBlockMenu menu)
        {
            return;
        }

        var container = await menu.CreateAsync(player, level, block, position);
        if (container is null)
            return;

        container = await StoredContainerAsync(player, level, position, container);
        if (player.Level != level || player is PlayerBase { Alive: false } or PlayerBase { Respawning: true })
            return;

        if (ReferenceEquals(menu, UseBehaviors.ChestBlock) && ChestBlocked(level, block, position))
            return;

        await player.OpenInventoryAsync(container);

        // Like vanilla, a spectator's look inside counts for nothing.
        if (player.GameMode != GameMode.Spectator && menu.Statistic(block) is string statistic)
            player.Statistics.Increment(statistic);
    }

    internal static Container NewChest(Vector position) => new()
    {
        Title = "Chest",
        BlockPosition = position,
        Id = "chest"
    };

    /// <summary>
    /// The inventory of the container block entity at <paramref name="position"/>, filled from the loot table it was
    /// waiting for, if any; or <paramref name="fresh"/> for blocks without one, whose menus each player gets their
    /// own of (anvils, enchanting tables).
    /// </summary>
    internal static async ValueTask<BaseContainer> StoredContainerAsync(
        IPlayer player,
        AbstractLevel level,
        Vector position,
        BaseContainer fresh)
    {
        // A double chest and the ender inventory are already what opens.
        if (fresh is DoubleChestContainer || fresh is Container { Id: "ender_chest" })
            return fresh;

        if (await level.GetOrCreateBlockEntityAsync(position) is not ContainerBlockEntity stored)
            return fresh;

        level.UnpackLootTable(stored, player);

        // The stored inventory itself is opened, so changes reach the block entity that's saved with the chunk.
        return stored.Inventory;
    }

    /// <summary>
    /// Joins a chest's container with its other half's, when the chest is half of a double chest, like vanilla: the
    /// neighbour its type and facing point to must be the same block, facing the same way, with the other type. The
    /// half whose type is <c>right</c> comes first.
    /// </summary>
    /// <returns>The double chest's container, or <paramref name="container"/> for a single chest.</returns>
    internal static async ValueTask<BaseContainer> DoubleChestAsync(
        IPlayer player,
        AbstractLevel level,
        IBlock block,
        Vector position,
        BaseContainer container)
    {
        var chestType = block.GetProperty("type");
        var facing = block.GetProperty("facing");
        if (chestType is not ("left" or "right") || ConnectedOffset(chestType, facing) is not Vector offset)
            return container;

        var neighbourPosition = position + offset;
        var neighbour = await level.GetBlockAsync(neighbourPosition);
        var neighbourType = neighbour?.GetProperty("type");

        if (neighbour is null
            || neighbour.RegistryId != block.RegistryId
            || neighbour.GetProperty("facing") != facing
            || neighbourType is not ("left" or "right")
            || neighbourType == chestType)
        {
            return container;
        }

        var other = await StoredContainerAsync(player, level, neighbourPosition, NewChest(neighbourPosition));

        var rightFirst = chestType == "right";
        var first = rightFirst ? container : other;
        var second = rightFirst ? other : container;
        var title = first.CustomName ?? second.CustomName ?? "Large Chest";

        return new DoubleChestContainer(first, second, title);
    }

    // Where a half of a double chest finds its other half: a left half clockwise of its facing, a right half
    // counter-clockwise, seen from above, like vanilla's ChestBlock.getConnectedDirection.
    private static Vector? ConnectedOffset(string chestType, string? facing)
    {
        string[] clockwise = ["north", "east", "south", "west"];
        var index = Array.IndexOf(clockwise, facing);
        if (index < 0)
            return null;

        var turn = chestType == "left" ? 1 : 3;

        return clockwise[(index + turn) % 4] switch
        {
            "north" => new Vector(0, 0, -1),
            "east" => new Vector(1, 0, 0),
            "south" => new Vector(0, 0, 1),
            _ => new Vector(-1, 0, 0)
        };
    }

    /// <summary>Vanilla chest obstruction applies to each valid half, including a sitting cat's bounding box.</summary>
    internal static bool ChestBlocked(AbstractLevel level, IBlock block, Vector position)
    {
        lock (level.Fluids.TickLock)
        {
            if (!level.BlockUpdates.GetBlock(position).IsSameState(block) || BlockedAt(position))
                return true;

            var type = block.GetProperty("type");
            if (type is not ("left" or "right")
                || ConnectedOffset(type, block.GetProperty("facing")) is not Vector offset)
            {
                return false;
            }

            var partnerPosition = position + offset;
            var partner = level.BlockUpdates.GetBlock(partnerPosition);
            return partner.RegistryId == block.RegistryId
                && partner.GetProperty("facing") == block.GetProperty("facing")
                && partner.GetProperty("type") == (type == "left" ? "right" : "left")
                && BlockedAt(partnerPosition);
        }

        bool BlockedAt(Vector at)
        {
            if (level.BlockUpdates.GetBlock(at + Vector.Up).IsRedstoneConductor())
                return true;

            var bounds = new BoundingBox((VectorD)(at + Vector.Up), (VectorD)at + new VectorD(1, 2, 1));
            return level.GetEntitiesInRange((VectorD)at + new VectorD(0.5, 1.5, 0.5), 3)
                .OfType<Cat>()
                .Any(cat => !cat.IsRemoved && cat.IsSitting
                    && MobTerrain.Overlaps(bounds, cat.Dimension.CreateBBFromPosition(cat.Position)));
        }
    }
}
