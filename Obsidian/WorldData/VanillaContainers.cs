using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.Events;
using Obsidian.Nbt;

namespace Obsidian.WorldData;

/// <summary>
/// A furnace's, blast furnace's or smoker's block entity, with vanilla's side rules
/// (<c>AbstractFurnaceBlockEntity</c>'s <c>WorldlyContainer</c>): the top reaches the ingredient, the sides the fuel,
/// the bottom the result and then the fuel (to take out an empty bucket).
/// </summary>
internal sealed partial class FurnaceBlockEntity : VanillaContainerBlockEntity
{
    private const int IngredientSlot = 0;
    private const int FuelSlot = 1;
    private const int ResultSlot = 2;

    private static readonly int[] up = [IngredientSlot];
    private static readonly int[] down = [ResultSlot, FuelSlot];
    private static readonly int[] sides = [FuelSlot];

    protected internal override IReadOnlyList<int> GetSlots(BlockFace? side) => side switch
    {
        null => base.GetSlots(side),
        BlockFace.Up => up,
        BlockFace.Down => down,
        _ => sides
    };

    // Vanilla's canPlaceItem, from any side: nothing goes into the result, and only fuel, or a bucket while there's
    // no bucket already, into the fuel slot.
    protected internal override bool CanInsert(int slot, ItemStack stack, BlockFace? side) => slot switch
    {
        ResultSlot => false,
        FuelSlot => MainEventHandler.IsFuel(stack)
            || stack.Is(ItemsRegistry.Bucket) && this.Inventory[FuelSlot]?.Is(ItemsRegistry.Bucket) != true,
        _ => true
    };

    // Vanilla's canTakeItemThroughFace: from below, the fuel slot gives up only a bucket, empty or of water.
    protected internal override bool CanExtract(int slot, ItemStack stack, BlockFace? side) =>
        side != BlockFace.Down
        || slot != FuelSlot
        || stack.Is(ItemsRegistry.WaterBucket)
        || stack.Is(ItemsRegistry.Bucket);
}

/// <summary>
/// A shulker box's block entity, which keeps its items when broken and, like vanilla's
/// (<c>ShulkerBoxBlockEntity.canPlaceItemThroughFace</c>), takes no shulker box through item access.
/// </summary>
internal sealed class ShulkerBoxBlockEntity(Container box) : VanillaContainerBlockEntity(box)
{
    protected internal override bool CanInsert(int slot, ItemStack stack, BlockFace? side) =>
        !TagsRegistry.Item.ShulkerBoxes.Entries.Contains(stack.Holder.Id);
}

/// <summary>
/// The brewing stand's side rules, like vanilla's <c>BrewingStandBlockEntity</c> as a <c>WorldlyContainer</c>: the
/// top reaches the ingredient, the bottom the bottles and the ingredient, the sides the bottles and the fuel.
/// </summary>
internal sealed partial class BrewingStandBlockEntity
{
    private static readonly int[] up = [BrewingStand.IngredientSlot];
    private static readonly int[] down = [0, 1, 2, BrewingStand.IngredientSlot];
    private static readonly int[] sides = [0, 1, 2, BrewingStand.FuelSlot];

    protected internal override IReadOnlyList<int> GetSlots(BlockFace? side) => side switch
    {
        null => base.GetSlots(side),
        BlockFace.Up => up,
        BlockFace.Down => down,
        _ => sides
    };

    // Vanilla's canPlaceItem: ingredients, fuel, and a bottle into an empty bottle slot.
    protected internal override bool CanInsert(int slot, ItemStack stack, BlockFace? side)
    {
        var recipes = this.Level is AbstractLevel level ? level.CookingRecipes : RecipesRegistry.Current;

        return slot switch
        {
            BrewingStand.IngredientSlot or BrewingStand.FuelSlot => Brewing.SlotAccepts(slot, stack, recipes),
            _ => Brewing.IsBottle(stack, recipes) && this.Inventory[slot] is null or { IsAir: true } or { Count: <= 0 }
        };
    }

    // Vanilla's canTakeItemThroughFace: through a face, the ingredient slot gives up only a glass bottle.
    protected internal override bool CanExtract(int slot, ItemStack stack, BlockFace? side) =>
        side is null || slot != BrewingStand.IngredientSlot || stack.Is(ItemsRegistry.GlassBottle);

    // A bottle slot holds one, like the menu's.
    protected internal override int SlotLimit(int slot, ItemStack stack) =>
        slot < BrewingStand.IngredientSlot ? 1 : base.SlotLimit(slot, stack);
}

/// <summary>
/// A hopper's block entity: its five slots, and the transfer cooldown it ticks with (see <see cref="Hoppers"/>),
/// saved as vanilla's <c>TransferCooldown</c>.
/// </summary>
internal sealed class HopperBlockEntity : VanillaContainerBlockEntity
{
    /// <summary>
    /// Vanilla's <c>HopperBlockEntity.MOVE_ITEM_SPEED</c>: the ticks between a hopper's transfers.
    /// </summary>
    public const int MoveItemSpeed = 8;

    public HopperBlockEntity(Vector position)
        : base(new Container(5) { Title = "Hopper", BlockPosition = position }, "TransferCooldown")
    {
    }

    /// <summary>
    /// The ticks until the hopper moves items again (vanilla's <c>cooldownTime</c>), -1 for a new one.
    /// </summary>
    public int Cooldown { get; set; } = -1;

    /// <summary>
    /// The game time the hopper last ticked at (vanilla's <c>tickedGameTime</c>), which isn't saved.
    /// </summary>
    public long TickedGameTime { get; set; }

    /// <summary>
    /// Vanilla's <c>isOnCustomCooldown</c>: a cooldown longer than a transfer's, which items arriving don't shorten.
    /// </summary>
    public bool OnCustomCooldown => this.Cooldown > MoveItemSpeed;

    protected internal override void Load(NbtCompound tag)
    {
        base.Load(tag);
        this.Cooldown = tag.TryGetNumber<int>("TransferCooldown", out var cooldown) ? cooldown : -1;
    }

    protected internal override void Save(NbtCompound tag)
    {
        base.Save(tag);
        tag.Add(new NbtTag<int>("TransferCooldown", this.Cooldown));
    }

    // Vanilla's tryMoveInItem: items arriving in an empty hopper hold its next transfer back a full cooldown.
    internal override void OnInserting()
    {
        if (!this.OnCustomCooldown && this.Inventory.All(stack => stack is null || stack.IsAir || stack.Count <= 0))
            this.Cooldown = MoveItemSpeed;
    }

    protected internal override ValueTask TickAsync() =>
        this.Level is AbstractLevel level ? Hoppers.TickAsync(level, this) : ValueTask.CompletedTask;
}
