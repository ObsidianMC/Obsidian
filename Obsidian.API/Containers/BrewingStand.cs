using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A brewing stand's block entity, whose slots vanilla's brewing menu numbers: the three bottles (0 to 2), the
/// ingredient (3) and the fuel (4). The server brews in it while its chunk ticks.
/// </summary>
public sealed class BrewingStand : ResultContainer
{
    // Its slots are its own, behind its own lock (see LocksOnlyItself).
    internal override bool LocksOnlyItself => true;

    /// <summary>
    /// The first of the three bottle slots.
    /// </summary>
    public const int FirstBottleSlot = 0;

    /// <summary>
    /// The ingredient slot.
    /// </summary>
    public const int IngredientSlot = 3;

    /// <summary>
    /// The fuel slot.
    /// </summary>
    public const int FuelSlot = 4;

    /// <summary>
    /// Gets the container's id, <c>brewing_stand</c>.
    /// </summary>
    public string Id => "brewing_stand";

    /// <summary>
    /// Gets or sets the position of the brewing stand block.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// The ticks the brew in progress has left, from <see cref="TotalBrewTime"/> down; 0 when nothing brews.
    /// </summary>
    public int BrewTime { get; set; }

    /// <summary>
    /// How long the brew in progress takes in all: 400 ticks, divided by the fuel's speed multiplier.
    /// </summary>
    public int TotalBrewTime { get; set; } = 400;

    /// <summary>
    /// How many more brews the fuel used so far lasts, up to <see cref="TotalFuel"/>.
    /// </summary>
    public int Fuel { get; set; }

    /// <summary>
    /// How many brews the fuel used last lasts in all (20 for blaze powder), from its <c>minecraft:brewing_fuel</c>.
    /// </summary>
    public int TotalFuel { get; set; } = 20;

    /// <summary>
    /// How much faster than normal the fuel used last brews, from its <c>minecraft:brewing_fuel</c>.
    /// </summary>
    public float SpeedMultiplier { get; set; } = 1f;

    /// <summary>
    /// The ingredient the brew in progress started with, which it stops without (vanilla's <c>ingredient</c>, which
    /// isn't saved either: loading a brew in progress takes it from the ingredient slot).
    /// </summary>
    internal Item? BrewingIngredient { get; set; }

    /// <summary>
    /// Which bottle slots held something when the block's state last showed them (vanilla's <c>lastPotionCount</c>).
    /// </summary>
    internal bool[]? ShownBottles { get; set; }

    /// <summary>
    /// Creates a brewing stand's five empty slots.
    /// </summary>
    public BrewingStand() : base(5, InventoryType.BrewingStand)
    {
        this.Title = "Brewing Stand";
    }

    /// <summary>
    /// Not supported: a brewing stand has no single result slot.
    /// </summary>
    /// <exception cref="NotImplementedException">Always.</exception>
    public override void SetResult(ItemStack? result) => throw new NotImplementedException();
    /// <summary>
    /// Not supported: a brewing stand has no single result slot.
    /// </summary>
    /// <exception cref="NotImplementedException">Always.</exception>
    public override ItemStack? GetResult() => throw new NotImplementedException();
}
