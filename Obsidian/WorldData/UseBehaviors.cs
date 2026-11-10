using Obsidian.API.Containers;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.Entities;

namespace Obsidian.WorldData;

/// <summary>
/// How a use went, in the shapes of vanilla's <c>InteractionResult</c>: whether it consumed the click, so nothing
/// after it runs, and how the arm swings.
/// </summary>
internal enum UseResult
{
    /// <summary>Nothing happened; what comes next (the item's use, placement) runs.</summary>
    Pass,

    /// <summary>The use was refused; nothing after it runs, and nothing swings.</summary>
    Fail,

    /// <summary>
    /// A block's use with the held item didn't apply: the block's use with an empty hand runs next, for the main hand.
    /// </summary>
    TryWithEmptyHand,

    /// <summary>The use happened; the user's client swung already, so the other players are shown the swing.</summary>
    Success,

    /// <summary>The use happened, and every player, the user included, is shown the swing.</summary>
    SuccessServer,

    /// <summary>The use happened without a swing.</summary>
    Consume
}

internal static class UseResults
{
    /// <summary>Whether the use is done with: nothing after it runs.</summary>
    internal static bool ConsumesAction(this UseResult result) =>
        result is UseResult.Success or UseResult.SuccessServer or UseResult.Consume;

    /// <summary>Whether the arm swings for the players around.</summary>
    internal static bool Swings(this UseResult result) =>
        result is UseResult.Success or UseResult.SuccessServer;
}

/// <summary>A player's use of a block: who, where, with which hand, and what they hold.</summary>
internal sealed class BlockUse
{
    public required IServer Server { get; init; }

    public required AbstractLevel Level { get; init; }

    public required Player Player { get; init; }

    /// <summary>The clicked block's position.</summary>
    public required Vector Position { get; init; }

    /// <summary>The clicked block, as it was read when the click arrived.</summary>
    public required IBlock Block { get; init; }

    /// <summary>The resident block entity captured with the clicked state, if any.</summary>
    public IBlockEntity? BlockEntity { get; init; }

    public required BlockFace Face { get; init; }

    /// <summary>Where on the block the click landed, from 0 to 1 on each axis.</summary>
    public required VectorD Cursor { get; init; }

    public required InteractionHand Hand { get; init; }

    /// <summary>The inventory slot of <see cref="Hand"/>.</summary>
    public required int HandSlot { get; init; }

    /// <summary>The stack in <see cref="Hand"/>, which may be empty.</summary>
    public required ItemStack? Item { get; init; }

    /// <summary>A copy of <see cref="Item"/> as it was before the use, which advancement triggers read.</summary>
    public required ItemStack? UsedItem { get; init; }

    /// <summary>The sequence number the client gave the click, which its acknowledgement carries back.</summary>
    public required int Sequence { get; init; }

    public bool HasItem => this.Item is { Count: > 0 };

    /// <summary>Whether the player can change the world: vanilla's <c>mayBuild</c>, false in adventure mode.</summary>
    public bool MayBuild => this.Player.GameMode is GameMode.Survival or GameMode.Creative;

    /// <summary>The center of the clicked block, where most of a use's game events and sounds come from.</summary>
    public VectorD Center => (VectorD)this.Position + new VectorD(0.5, 0.5, 0.5);

    /// <summary>
    /// Set when the client's prediction of the hand may be wrong, so its slot is sent again along with the blocks.
    /// </summary>
    public bool ResyncHand { get; set; }

    /// <summary>
    /// Whether the item's successful use awards its <c>minecraft:used</c> statistic. Plugins' items keep their own
    /// statistics.
    /// </summary>
    public bool AwardsItemUse { get; set; } = true;

    /// <summary>A queued action owns rewards and the packet response once it commits or refuses the use.</summary>
    public bool CompletionDeferred { get; set; }

    public bool CanCommit => this.Player.Connected && this.Player.Alive && !this.Player.Respawning
        && this.Player.Level == this.Level
        && this.Level.Players.TryGetValue(this.Player.Uuid, out var resident)
        && ReferenceEquals(resident, this.Player);

    /// <summary>
    /// A queued action starts against the state earlier actions left, under the level tick lock. The original
    /// hand reference and component snapshot remain required; the count is sampled for this action and checked
    /// again under the inventory lock by TryCommit.
    /// </summary>
    internal BlockUse ForQueuedAction() => new()
    {
        Server = this.Server,
        Level = this.Level,
        Player = this.Player,
        Position = this.Position,
        Block = this.Level.BlockUpdates.GetBlock(this.Position),
        BlockEntity = this.Level.GetResidentBlockEntity(this.Position),
        Face = this.Face,
        Cursor = this.Cursor,
        Hand = this.Hand,
        HandSlot = this.HandSlot,
        Item = this.Item,
        UsedItem = this.UsedItem is null ? null : new ItemStack(this.UsedItem, this.Item?.Count ?? 0),
        Sequence = this.Sequence,
        AwardsItemUse = this.AwardsItemUse
    };

    /// <summary>
    /// Commits a synchronous destination change and its hand exchange together. The tick lock comes first, then
    /// the hand and destination inventories. Busy inventories refuse the click rather than waiting under the tick
    /// lock. A state-only write scope suppresses external effects, including the normal block notifications.
    /// Those effects are published in write order through the normal level path after both inventories unlock.
    /// The callback validates before writing and never awaits.
    /// </summary>
    /// <remarks>
    /// A replacement is only for a single unstackable input, such as a filled fish bucket. Cauldrons and hives
    /// need a separate exchange operation that consumes one and routes the result to the hand, inventory or world;
    /// those batches must add that operation outside this critical section's callback.
    /// </remarks>
    public bool TryCommit(Func<bool> change, bool consumeItem = false, BaseContainer? destination = null,
        ItemStack? replacement = null)
    {
        lock (this.Level.Fluids.TickLock)
        {
            if (!this.CanCommit
                || this.Hand == InteractionHand.MainHand && this.Player.CurrentHeldItemSlot != this.HandSlot
                || this.BlockEntity is BlockEntity { IsRemoved: true }
                || !this.Level.BlockUpdates.GetBlock(this.Position).IsSameState(this.Block)
                || !ReferenceEquals(this.Level.GetResidentBlockEntity(this.Position), this.BlockEntity)
                || !this.Player.Inventory.TryLock(out var handLock))
            {
                this.ResyncHand = true;
                return false;
            }

            BlockUseEffects? effects = null;
            using (handLock)
            {
                var held = this.Player.Inventory.GetItem(this.HandSlot);
                if (!ReferenceEquals(held, this.Item)
                    || held is not null && (held.Count <= 0 || held.Count != this.UsedItem?.Count
                        || !held.Equals(this.UsedItem))
                    || consumeItem && held is null
                    || replacement is not null && (!consumeItem || held?.Count != 1))
                {
                    this.ResyncHand = true;
                    return false;
                }

                IDisposable? destinationLock = null;
                if (destination is not null && !destination.TryLock(out destinationLock))
                {
                    this.ResyncHand = true;
                    return false;
                }

                using (destinationLock)
                using (effects = new BlockUseEffects(this.Level))
                {
                    if (destination?.ShowsRetired == true || !change())
                    {
                        this.ResyncHand = true;
                        return false;
                    }

                    if (consumeItem && this.Player.GameMode != GameMode.Creative)
                    {
                        if (replacement is null)
                            this.Player.Inventory.RemoveItem(this.HandSlot, 1);
                        else
                            this.Player.Inventory.SetItem(this.HandSlot, replacement);

                        this.ResyncHand = true;
                    }

                }
            }

            effects!.Publish();
            return true;
        }
    }

    /// <summary>Awards one of a <c>minecraft:custom</c> statistic, such as <c>minecraft:open_chest</c>.</summary>
    public void Award(string statistic) => this.Player.Statistics.Increment(statistic);
}

/// <summary>
/// A vanilla class's <c>useItemOn</c> and <c>useWithoutItem</c> overrides, as Obsidian runs them when a player uses
/// a block it owns. Each block is bound to a member of <see cref="UseBehaviors"/> per hook, by the class that
/// declares the hook's override, so a behaviour serves only the hooks its class overrides.
/// </summary>
internal interface IBlockUseBehavior
{
    /// <summary>
    /// The block's use with the held item, which runs first. <see cref="UseResult.TryWithEmptyHand"/> passes the
    /// use to <see cref="UseWithoutItemAsync"/> for the main hand.
    /// </summary>
    ValueTask<UseResult> UseItemOnAsync(BlockUse use) => ValueTask.FromResult(UseResult.TryWithEmptyHand);

    /// <summary>The block's use regardless of the held item, for the main hand.</summary>
    ValueTask<UseResult> UseWithoutItemAsync(BlockUse use) => ValueTask.FromResult(UseResult.Pass);
}

/// <summary>A vanilla class's <c>attack</c> override, run when a player starts hitting a block it owns.</summary>
internal interface IBlockAttackBehavior
{
    ValueTask AttackAsync(AbstractLevel level, Vector position, IBlock block, PlayerBase player);
}

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>useItemOn</c> or <c>useWithoutItem</c>, bound
/// from <c>block_use_item_on_classes.json</c> and <c>block_use_without_item_classes.json</c>
/// (<c>BlocksRegistry.GetUseItemOnBehavior</c> and <c>GetUseWithoutItemBehavior</c>). A class without a member
/// here does nothing when used, and the held item's use runs.
/// </summary>
internal static partial class UseBehaviors
{
    internal static IBlockUseBehavior DoorBlock { get; } = new OpenableUse();

    internal static IBlockUseBehavior TrapDoorBlock { get; } = new OpenableUse();

    internal static IBlockUseBehavior FenceGateBlock { get; } = new OpenableUse();

    internal static IBlockUseBehavior LeverBlock { get; } = new RedstoneComponentUse();

    internal static IBlockUseBehavior ButtonBlock { get; } = new RedstoneComponentUse();

    internal static IBlockUseBehavior RepeaterBlock { get; } = new RedstoneComponentUse();

    internal static IBlockUseBehavior ComparatorBlock { get; } = new RedstoneComponentUse();

    internal static IBlockUseBehavior DaylightDetectorBlock { get; } = new RedstoneComponentUse();

    internal static IBlockUseBehavior NoteBlock { get; } = new NoteBlockUse();

    internal static IBlockUseBehavior JukeboxBlock { get; } = new JukeboxUse();

    internal static IBlockUseBehavior CampfireBlock { get; } = new CampfireUse();

    internal static IBlockUseBehavior ComposterBlock { get; } = new ComposterUse();

    internal static IBlockUseBehavior AbstractBedBlock { get; } = new BedUse();

    internal static IBlockUseBehavior RespawnAnchorBlock { get; } = new RespawnAnchorUse();

    internal static IBlockUseBehavior SignBlock { get; } = new SignUse();

    internal static IBlockUseBehavior CeilingHangingSignBlock { get; } = new SignUse();

    internal static IBlockUseBehavior WallHangingSignBlock { get; } = new SignUse();

    internal static IBlockUseBehavior TntBlock { get; } = new TntUse();

    internal static IBlockUseBehavior FenceBlock { get; } = new FenceUse();

    internal static IBlockUseBehavior LecternBlock { get; } = new LecternUse();

    internal static IBlockUseBehavior ChestBlock { get; } = new ChestMenuUse();

    internal static IBlockUseBehavior EnderChestBlock { get; } = new EnderChestMenuUse();

    internal static IBlockUseBehavior BarrelBlock { get; } = new MenuUse("minecraft:open_barrel");

    internal static IBlockUseBehavior ShulkerBoxBlock { get; } = new MenuUse("minecraft:open_shulker_box");

    internal static IBlockUseBehavior DispenserBlock { get; } = new DispenserMenuUse();

    internal static IBlockUseBehavior HopperBlock { get; } = new MenuUse(
        "minecraft:inspect_hopper",
        (_, position) => new Container(5) { BlockPosition = position });

    internal static IBlockUseBehavior AbstractFurnaceBlock { get; } = new FurnaceMenuUse();

    internal static IBlockUseBehavior BrewingStandBlock { get; } = new MenuUse(
        "minecraft:interact_with_brewingstand",
        (_, position) => new BrewingStand { BlockPosition = position });

    internal static IBlockUseBehavior CraftingTableBlock { get; } = new MenuUse(
        "minecraft:interact_with_crafting_table",
        (_, _) => new CraftingTable());

    internal static IBlockUseBehavior AnvilBlock { get; } = new MenuUse(
        "minecraft:interact_with_anvil",
        (_, position) => new AnvilContainer { BlockPosition = position });

    internal static IBlockUseBehavior GrindstoneBlock { get; } = new MenuUse(
        "minecraft:interact_with_grindstone",
        (_, position) => new Grindstone { BlockPosition = position });

    internal static IBlockUseBehavior StonecutterBlock { get; } = new MenuUse(
        "minecraft:interact_with_stonecutter",
        (_, position) => new Stonecutter { BlockPosition = position });

    internal static IBlockUseBehavior LoomBlock { get; } = new MenuUse(
        "minecraft:interact_with_loom",
        (_, position) => new Loom { BlockPosition = position });

    internal static IBlockUseBehavior CartographyTableBlock { get; } = new MenuUse(
        "minecraft:interact_with_cartography_table",
        (_, position) => new CartographyTable { BlockPosition = position });

    internal static IBlockUseBehavior SmithingTableBlock { get; } = new MenuUse(
        "minecraft:interact_with_smithing_table",
        (_, position) => new SmithingTable { BlockPosition = position });

    internal static IBlockUseBehavior EnchantingTableBlock { get; } = new MenuUse(
        statistic: null,
        (_, position) => new EnchantmentTable { BlockPosition = position });

    internal static IBlockUseBehavior BeaconBlock { get; } = new MenuUse(
        "minecraft:interact_with_beacon",
        (_, position) => new BeaconContainer { BlockPosition = position });

    // The crafter's block entity holds its own container (see BlockMenus.StoredContainerAsync).
    internal static IBlockUseBehavior CrafterBlock { get; } = new MenuUse(
        statistic: null,
        (_, position) => new CrafterContainer { BlockPosition = position });
}

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>attack</c>, bound from
/// <c>block_attack_classes.json</c> (<c>BlocksRegistry.GetAttackBehavior</c>).
/// </summary>
internal static class AttackBehaviors
{
    internal static IBlockAttackBehavior NoteBlock { get; } = new UseBehaviors.NoteBlockUse();
}
