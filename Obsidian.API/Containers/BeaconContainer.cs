using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A beacon's window, like vanilla's <c>BeaconMenu</c>: its payment slot, which takes one of the beacon's payment
/// items to choose its powers.
/// </summary>
public sealed class BeaconContainer : BaseContainer
{
    public const int PaymentSlot = 0;

    public Vector BlockPosition { get; set; }

    public BeaconContainer() : base(1, InventoryType.Beacon)
    {
        this.Title = "Beacon";
    }
}
