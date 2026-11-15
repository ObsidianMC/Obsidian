using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A furnace's, smoker's or blast furnace's slots: the ingredient in slot 0, the fuel in slot 1 and the result in slot
/// 2.
/// </summary>
public class SmeltingContainer : ResultContainer
{
    // Its slots are its own, behind its own lock (see LocksOnlyItself). Only for this exact type: a subclass can
    // override GetItem and SetItem to keep its slots elsewhere, so it goes through Lock like any other container.
    internal sealed override bool LocksOnlyItself => this.GetType() == typeof(SmeltingContainer);

    /// <summary>
    /// Gets the container's id, like <c>furnace</c>.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets or sets a fuel time. The server keeps its furnaces' progress in their block entities and doesn't read this.
    /// </summary>
    public short FuelBurnTime { get; set; }

    /// <summary>
    /// Gets or sets a cooking time. The server keeps its furnaces' progress in their block entities and doesn't read
    /// this.
    /// </summary>
    public short CookTime { get; set; }

    /// <summary>
    /// Gets or sets a total cooking time. The server keeps its furnaces' progress in their block entities and doesn't
    /// read this.
    /// </summary>
    public short CookTimeTotal { get; set; }

    /// <summary>
    /// Gets or sets the position of the furnace block.
    /// </summary>
    public Vector BlockPosition { get; set; }

    internal SmeltingContainer(InventoryType type, string id) : this(type, 3, id) { }

    /// <summary>
    /// Creates the empty slots of a furnace-like container.
    /// </summary>
    /// <param name="type">How it opens: <see cref="InventoryType.Furnace"/>, <see cref="InventoryType.Smoker"/>,
    /// <see cref="InventoryType.BlastFurnace"/> or <see cref="InventoryType.Custom"/>.</param>
    /// <param name="size">The number of slots.</param>
    /// <param name="id">The container's id, like <c>furnace</c>.</param>
    /// <exception cref="InvalidOperationException"><paramref name="type"/> isn't one of those.</exception>
    public SmeltingContainer(InventoryType type, int size, string id) : base(size, type)
    {
        if (type is not InventoryType.Furnace and not InventoryType.Smoker and not InventoryType.BlastFurnace and not InventoryType.Custom)
            throw new InvalidOperationException("Type must either be custom, furnace, blast furnace or smoker.");

        this.Id = id;
    }

    /// <summary>
    /// Not implemented; read the fuel slot with <see cref="BaseContainer.GetItem"/>.
    /// </summary>
    /// <exception cref="NotImplementedException">Unless a subclass overrides it.</exception>
    public virtual ItemStack? GetFuel() => throw new NotImplementedException();
    /// <summary>
    /// Not implemented; read the ingredient slot with <see cref="BaseContainer.GetItem"/>.
    /// </summary>
    /// <exception cref="NotImplementedException">Unless a subclass overrides it.</exception>
    public virtual ItemStack?[] GetIngredients() => throw new NotImplementedException();

    /// <summary>
    /// Not implemented; set the result slot with <see cref="BaseContainer.SetItem"/>.
    /// </summary>
    /// <exception cref="NotImplementedException">Always.</exception>
    public override void SetResult(ItemStack? result) => throw new NotImplementedException();
    /// <summary>
    /// Not implemented; read the result slot with <see cref="BaseContainer.GetItem"/>.
    /// </summary>
    /// <exception cref="NotImplementedException">Always.</exception>
    public override ItemStack? GetResult() => throw new NotImplementedException();

}
