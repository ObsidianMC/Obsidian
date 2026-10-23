namespace Obsidian.API.Client;

/// <summary>
/// Where an item is drawn, like vanilla's <c>ItemDisplayContext</c>: the names an item model definition's
/// <c>minecraft:display_context</c> property and a model's <c>display</c> transforms use, in snake case
/// (<c>thirdperson_righthand</c>, <c>gui</c>, ...).
/// </summary>
public enum ItemDisplayContext
{
    None,
    ThirdPersonLeftHand,
    ThirdPersonRightHand,
    FirstPersonLeftHand,
    FirstPersonRightHand,
    Head,
    Gui,
    Ground,
    Fixed,
    OnShelf
}

/// <summary>
/// What an item is drawn for, besides its stack: given to a mod's item model properties (see
/// <see cref="IClientRegistry.RegisterItemCondition"/>) and item renderers (see <see cref="IItemRenderer"/>).
/// </summary>
public readonly record struct ItemModelContext
{
    /// <summary>
    /// Where the item is drawn.
    /// </summary>
    public ItemDisplayContext DisplayContext { get; init; }

    /// <summary>
    /// The entity holding or showing the stack, as <see cref="IClientWorld.GetEntity"/> takes it: the mob or player
    /// holding or wearing it, the item frame showing it, or the dropped item; the player for their inventory and
    /// hotbar. Null when there's none, like in menus outside a world.
    /// </summary>
    public int? HolderId { get; init; }

    /// <summary>
    /// The world's game time, in ticks, as the server's level counts them (<c>ILevel.Time</c>), which the client
    /// follows; 0 outside a world.
    /// </summary>
    public long GameTime { get; init; }

    /// <summary>
    /// The time of day, in ticks: 0 at sunrise, 6000 at noon, counting on across days; 0 outside a world.
    /// </summary>
    public long DayTime { get; init; }
}
