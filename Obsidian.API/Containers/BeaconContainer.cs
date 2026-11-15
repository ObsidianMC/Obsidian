using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A beacon's window, like vanilla's <c>BeaconMenu</c>: its payment slot, which takes one of the beacon's payment
/// items to choose its powers.
/// </summary>
public sealed class BeaconContainer : BaseContainer
{
    /// <summary>
    /// The payment slot's index.
    /// </summary>
    public const int PaymentSlot = 0;

    /// <summary>
    /// Gets or sets the position of the beacon the window belongs to.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// Creates a beacon window's payment slot.
    /// </summary>
    public BeaconContainer() : base(1, InventoryType.Beacon)
    {
        this.Title = "Beacon";
    }
}
