using Obsidian.API.Inventory;

namespace Obsidian.WorldData;

internal sealed partial class LevelBlockUpdates
{
    /// <inheritdoc/>
    public IItemAccess? GetItemAccess(Vector position, BlockFace? side) => this.Level.GetItemAccess(position, side);
}
