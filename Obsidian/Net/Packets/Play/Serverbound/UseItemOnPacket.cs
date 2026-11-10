using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// A player's right click on a block. Like vanilla's <c>ServerPlayerGameMode.useItemOn</c>, the block's own use comes
/// first (bound per block from the vanilla class that declares it, see <see cref="UseBehaviors"/>), then the held
/// item's use on the block, then placement; one step then awards what a successful use owes and answers the client.
/// </summary>
public partial class UseItemOnPacket
{
    private const int OffhandSlot = 45;

    // Vanilla's handleUseItemOn: how far past the block the clicked point may be, on any axis, before the click is
    // refused as impossible.
    private const double CursorLimit = 1.0000001;

    [Field(0), ActualType(typeof(int)), VarLength]
    public InteractionHand Hand { get; private set; } // Hand it was placed from. 0 = Main, 1 = Off

    [Field(1)]
    public Vector Position { get; private set; }

    [Field(2), ActualType(typeof(int)), VarLength]
    public BlockFace Face { get; private set; }

    [Field(3), DataFormat(typeof(float))]
    public VectorD Cursor { get; private set; }

    [Field(6)]
    public bool InsideBlock { get; private set; }

    [Field(7)]
    public bool IsWorldBorderHit { get; private set; }

    [Field(8), VarLength]
    public int Sequence { get; private set; }

    /// <summary>
    /// Whether sneaking with an item in either hand skips using the block there: the item is used or placed instead,
    /// as in vanilla. Sneaking with empty hands still uses the block.
    /// </summary>
    internal static bool BypassesBlockUse(bool sneaking, ItemStack? mainHand, ItemStack? offHand) =>
        sneaking && (mainHand is { Count: > 0 } || offHand is { Count: > 0 });

    public override void Populate(INetStreamReader reader)
    {
        this.Hand = reader.ReadVarInt<InteractionHand>();
        this.Position = reader.ReadPosition();
        this.Face = reader.ReadVarInt<BlockFace>();
        this.Cursor = reader.ReadAbsoluteFloatPositionF();
        this.InsideBlock = reader.ReadBoolean();
        this.IsWorldBorderHit = reader.ReadBoolean();
        this.Sequence = reader.ReadVarInt();
    }

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        await this.UseAsync(server, player);

    /// <summary>Handles the click and returns how it went, or null when it was refused before any use.</summary>
    internal async ValueTask<UseResult?> UseAsync(IServer server, IClientPlayer player)
    {
        if (player is not Player user || user.Level is not AbstractLevel level)
            return null;

        // Acknowledgements belong to received sequences, not to completions that may be deferred.
        user.QueueBlockAcknowledgement(this.Sequence);

        if (!user.Alive || user.Respawning || user.HasPendingTeleport)
        {
            await this.RefuseAsync(user, level);
            return null;
        }

        // Like vanilla's handleUseItemOn, a click out of reach, beyond the clicked block or outside the build height
        // is acknowledged and otherwise ignored.
        var block = this.InReach(user) && !level.IsOutsideBuildHeight(this.Position.Y)
            ? await level.GetBlockAsync(this.Position)
            : null;
        if (block is null || !user.Alive || user.Respawning || user.HasPendingTeleport)
        {
            await this.RefuseAsync(user, level);
            return null;
        }

        int handSlot;
        ItemStack? item;
        ItemStack? usedItem;
        using (await user.Inventory.LockAsync())
        {
            handSlot = this.Hand == InteractionHand.OffHand ? OffhandSlot : user.CurrentHeldItemSlot;
            item = user.Inventory.GetItem(handSlot);
            usedItem = item is null ? null : new ItemStack(item, item.Count);
        }

        if (!user.Alive || user.Respawning || user.HasPendingTeleport)
        {
            await this.RefuseAsync(user, level);
            return null;
        }

        // The inventory and world snapshots need no shared lock: exchanges validate both again at commitment.
        IBlockEntity? blockEntity;
        lock (level.Fluids.TickLock)
        {
            block = level.BlockUpdates.GetBlock(this.Position);
            blockEntity = level.GetResidentBlockEntity(this.Position);
        }

        var use = new BlockUse
        {
            Server = server,
            Level = level,
            Player = user,
            Position = this.Position,
            Block = block,
            BlockEntity = blockEntity,
            Face = this.Face,
            Cursor = this.Cursor,
            Hand = this.Hand,
            HandSlot = handSlot,
            Item = item,
            UsedItem = usedItem,
            Sequence = this.Sequence
        };

        var result = await this.UseBlockAsync(use);
        if (!use.CompletionDeferred)
            await CompleteAsync(use, result);

        return result;
    }

    // Vanilla's Player.isWithinBlockInteractionRange with a block of leeway, and the clicked point no farther than a
    // block from the clicked block's center.
    private bool InReach(PlayerBase player)
    {
        var range = PlayerBase.BlockInteractionRange(player) + 1;
        var eye = PlayerBase.EyePosition(player);
        var min = (VectorD)this.Position;
        var dx = Math.Max(Math.Max(min.X - eye.X, eye.X - (min.X + 1)), 0);
        var dy = Math.Max(Math.Max(min.Y - eye.Y, eye.Y - (min.Y + 1)), 0);
        var dz = Math.Max(Math.Max(min.Z - eye.Z, eye.Z - (min.Z + 1)), 0);
        if (dx * dx + dy * dy + dz * dz >= range * range)
            return false;

        return Math.Abs(this.Cursor.X - 0.5) < CursorLimit
            && Math.Abs(this.Cursor.Y - 0.5) < CursorLimit
            && Math.Abs(this.Cursor.Z - 0.5) < CursorLimit;
    }

    /// <summary>
    /// Vanilla's <c>ServerPlayerGameMode.useItemOn</c>: a spectator only opens menus; otherwise the block is used
    /// with the item, then without it (main hand only), unless the player sneaks with something in hand; then the
    /// item is used on the block.
    /// </summary>
    private async ValueTask<UseResult> UseBlockAsync(BlockUse use)
    {
        var player = use.Player;
        if (player.GameMode == GameMode.Spectator)
            return await SpectateAsync(use);

        var bypass = BypassesBlockUse(player.Sneaking, player.Inventory.GetItem(player.CurrentHeldItemSlot),
            player.Inventory.GetItem(OffhandSlot));
        if (!bypass)
        {
            var withItem = await UseWithItemAsync(use);
            if (withItem == UseResult.Fail)
                return withItem;

            if (withItem.ConsumesAction())
            {
                await FireAsync(use, "item_used_on_block");
                return withItem;
            }

            var withoutItem = withItem == UseResult.TryWithEmptyHand && use.Hand == InteractionHand.MainHand
                ? await UseWithoutItemAsync(use)
                : UseResult.Pass;
            if (withoutItem == UseResult.Fail)
                return withoutItem;

            if (withoutItem.ConsumesAction())
            {
                var current = await use.Level.GetBlockAsync(use.Position);
                await player.FireProgressAsync("default_block_use", block: current, position: use.Position);
                return withoutItem;
            }
        }

        if (!use.HasItem || player.IsOnCooldown(use.Item))
            return UseResult.Pass;

        var itemResult = await this.UseItemAsync(use);
        if (itemResult.ConsumesAction() && !use.CompletionDeferred)
        {
            if (use.AwardsItemUse)
                player.Statistics.Increment(use.Item!.Holder.UnlocalizedName, type: "minecraft:used");

            await FireAsync(use, "item_used_on_block");
        }

        return itemResult;
    }

    // The block's use with the held item: a plugin's block decides for itself, a vanilla block by its class.
    private static async ValueTask<UseResult> UseWithItemAsync(BlockUse use)
    {
        if (Plugins.ItemBehaviors.CanUseItems(use.Player))
        {
            var used = await Plugins.BlockBehaviors.UseAsync(new BlockUseContext
            {
                Player = use.Player,
                Position = use.Position,
                Block = use.Block,
                Face = use.Face,
                Hand = use.Hand,
                ClickedAt = use.Cursor
            });

            if (used)
                return UseResult.Success;
        }

        var behavior = BlocksRegistry.GetUseItemOnBehavior(use.Block.RegistryId);
        if (!use.CanCommit)
            return UseResult.Fail;

        if (behavior is null)
            return UseResult.TryWithEmptyHand;

        return await behavior.UseItemOnAsync(use);
    }

    // The block's use regardless of the held item.
    private static ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
    {
        var behavior = BlocksRegistry.GetUseWithoutItemBehavior(use.Block.RegistryId);
        if (!use.CanCommit)
            return ValueTask.FromResult(UseResult.Fail);

        if (behavior is null)
            return ValueTask.FromResult(UseResult.Pass);

        return behavior.UseWithoutItemAsync(use);
    }

    // Vanilla's spectators: a block with a menu opens it, and nothing else happens.
    private static async ValueTask<UseResult> SpectateAsync(BlockUse use)
    {
        var behavior = BlocksRegistry.GetUseWithoutItemBehavior(use.Block.RegistryId);
        if (behavior is not IBlockMenu { OpensForSpectators: true })
        {
            use.ResyncHand = true;
            return UseResult.Pass;
        }

        await BlockMenus.RequestAsync(use);
        return UseResult.Consume;
    }

    /// <summary>
    /// What every use owes once it's done, like vanilla's <c>handleUseItemOn</c>: the <c>any_block_use</c> trigger
    /// and the arm's swing when it consumed the click, then the clicked block and the one beside it, which the
    /// client may have predicted wrongly. Received sequences are acknowledged once per player tick.
    /// </summary>
    private static async ValueTask CompleteAsync(BlockUse use, UseResult result)
    {
        var player = use.Player;
        if (result.ConsumesAction())
        {
            var current = await use.Level.GetBlockAsync(use.Position);
            await player.FireProgressAsync("any_block_use", item: use.Item, block: current, position: use.Position);
        }

        if (result.Swings())
            player.SwingForUse(use.Hand, use.Item, includeSelf: result == UseResult.SuccessServer);

        ResendBlocks(use.Level, player, use.Position, use.Face);
        if (use.ResyncHand || !result.ConsumesAction())
            await player.SyncInventorySlotAsync(use.HandSlot);
    }

    private static async ValueTask FireAsync(BlockUse use, string trigger)
    {
        var current = await use.Level.GetBlockAsync(use.Position);
        await use.Player.FireProgressAsync(trigger, item: use.UsedItem, block: current, position: use.Position);
    }

    // Refusals repair the client's prediction before acknowledging, including reach and lifecycle refusals.
    private async ValueTask RefuseAsync(Player player, AbstractLevel level)
    {
        ResendBlocks(level, player, this.Position, this.Face);
        var slot = this.Hand == InteractionHand.OffHand ? OffhandSlot : player.CurrentHeldItemSlot;
        await player.SyncInventorySlotAsync(slot);
    }

    // Read only loaded state: an impossible click must not load distant chunks just to correct its prediction.
    private static void ResendBlocks(AbstractLevel level, Player player, Vector clicked, BlockFace face)
    {
        var beside = clicked + face.ToVector();
        foreach (var position in new[] { clicked, beside })
        {
            var block = level.GameEventBlock(position) ?? BlocksRegistry.Air;
            player.Client.SendPacket(new BlockUpdatePacket(position, block.GetHashCode()));
        }
    }
}
