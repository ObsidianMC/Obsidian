using Obsidian.API.Inventory;

namespace Obsidian.API.Events;

/// <summary>
/// A player's click in their open window, raised before the server applies it.
/// </summary>
/// <remarks>
/// <para>
/// Handlers run outside the container's lock, so a handler may wait, even for another viewer's click. The server's
/// own handler runs last: inside the lock it checks that the window is still open and that neither the container
/// nor the player's inventory changed since the click arrived (see <see cref="BaseContainer.Revision"/>), then
/// applies the click, unless it's cancelled, and sends the window's contents and cursor to its viewers. A click
/// whose slots changed meanwhile isn't applied, so a handler's decision is never applied to contents it didn't see;
/// the player's window is corrected instead, like after a cancelled click.
/// </para>
/// <para>
/// A handler may cancel the click and read <see cref="Item"/>, which is what the slot held as the click arrived.
/// To change slots as part of the click, register the change with <see cref="ApplyWithClick"/>, which runs it inside
/// the lock with the click. A handler that changes the container itself, through its methods, makes its own click
/// stale, and its change may race other viewers' clicks.
/// </para>
/// </remarks>
public sealed class ContainerClickEventArgs : ContainerEventArgs, ICancellable
{
    private readonly List<Action> changes = [];

    private bool arrived;

    private ItemStack? arrivalItem;

    /// <summary>
    /// What the clicked slot held when the click arrived, as a copy: changing it changes nothing.
    /// </summary>
    public ItemStack? Item => this.arrived ? this.arrivalItem : this.SlotItem;

    /// <summary>
    /// What the clicked slot holds now, which the server's handler changes.
    /// </summary>
    internal ItemStack? SlotItem => this.ClickedSlot >= 0 ? this.Container.GetItem(this.ClickedSlot) : null;

    public bool IsPlayerInventory => this.ContainerId == 0;

    /// <summary>
    /// The ID of the inventory. Usually incremented by one every time an inventory is opened and resets when it reaches 255.
    /// </summary>
    /// <remarks>
    /// This value is unique per client.
    /// </remarks>
    public required int ContainerId { get; init; }

    /// <summary>
    /// Gets the slot that was clicked
    /// </summary>
    public required short ClickedSlot { get; init; }

    /// <summary>
    /// The clicked slot as the open window numbers it: the open container's slots, then the player's inventory.
    /// <see cref="ClickedSlot"/> is the same slot in <see cref="ContainerEventArgs.Container"/>.
    /// </summary>
    internal short WindowSlot { get; init; }

    /// <summary>
    /// The button that was clicked in the inventory. Can vary depending on the inventory type.
    /// </summary>
    public required sbyte Button { get; init; }

    public required int StateId { get; init; }

    public required ClickType ClickType { get; init; }

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    /// <summary>
    /// The open window's <see cref="BaseContainer.Revision"/> when the click arrived.
    /// </summary>
    internal long WindowRevision { get; private set; }

    /// <summary>
    /// The player's inventory's <see cref="BaseContainer.Revision"/> when the click arrived.
    /// </summary>
    internal long InventoryRevision { get; private set; }

    /// <summary>
    /// The changes plugins registered with <see cref="ApplyWithClick"/>, in order.
    /// </summary>
    internal IReadOnlyList<Action> Changes => this.changes;

    internal ContainerClickEventArgs(IPlayer player, IServer server, BaseContainer container) : base(player, server, container) { }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }

    /// <summary>
    /// Registers a change to make with the click: the server runs <paramref name="change"/> inside the container's
    /// lock, right after applying the click, or in its place when the click is cancelled, and only if the click's
    /// slots didn't change since it arrived, so the change acts on what <see cref="Item"/> shows. The window's viewers
    /// then see the result.
    /// </summary>
    /// <param name="change">
    /// The change, which reads and sets slots through the container's methods. It runs on the server's handler, so it
    /// must not block or wait.
    /// </param>
    public void ApplyWithClick(Action change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (this.changes)
            this.changes.Add(change);
    }

    /// <summary>
    /// Remembers the window's state as the click arrives: the revisions it is checked against before it's applied,
    /// and a copy of the clicked slot's stack for <see cref="Item"/>.
    /// </summary>
    /// <param name="window">The player's open window.</param>
    internal void Arrive(BaseContainer window)
    {
        this.WindowRevision = window.Revision;
        this.InventoryRevision = this.Player.Inventory.Revision;

        var item = this.SlotItem;
        this.arrivalItem = item is null ? null : new ItemStack(item, item.Count);
        this.arrived = true;
    }

    internal static ContainerClickEventArgs Create(
        IPlayer player,
        IServer server,
        BaseContainer container,
        int containerId,
        short clickedSlot,
        sbyte button,
        int stateId,
        ClickType clickType)
    {
        return new ContainerClickEventArgs(player, server, container)
        {
            ContainerId = containerId,
            ClickedSlot = clickedSlot,
            WindowSlot = clickedSlot,
            Button = button,
            StateId = stateId,
            ClickType = clickType
        };
    }
}
