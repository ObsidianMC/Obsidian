using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

/// <summary>
/// Reads shape survival without changing the level's ticks or random sequence. Random values are used only for
/// discarded scheduling delays by the current rules; a private source keeps even those draws away from the level.
/// </summary>
internal class ShapeValidationView(IShapeLevel level) : IShapeLevel
{
    public IRandomSource Random { get; } = new LegacyRandomSource(0);

    public IBlock GetBlock(Vector position) => level.GetBlock(position);

    public bool IsFaceSturdy(Vector position, BlockFace face, SupportType support = SupportType.Full) =>
        level.IsFaceSturdy(position, face, support);

    public IBlockEntity? GetBlockEntity(Vector position) => level.GetBlockEntity(position);

    public int GetRawBrightness(Vector position) => level.GetRawBrightness(position);

    public void ScheduleTick(
        Vector position,
        IBlock block,
        int delay,
        TickPriority priority = TickPriority.Normal)
    {
    }

    public bool HasScheduledTick(Vector position, IBlock block) => level.HasScheduledTick(position, block);
}

/// <summary>The same validation view for plugin hooks, which receive the wider block-level interface.</summary>
internal sealed class BlockValidationView(LevelBlockUpdates level) : ShapeValidationView(level), IBlockLevel
{
    public ILevel Level => level.Level;

    public int GetSignal(Vector position, BlockFace direction) => level.GetSignal(position, direction);

    public int GetDirectSignal(Vector position, BlockFace direction) => level.GetDirectSignal(position, direction);

    public int GetDirectSignalTo(Vector position) => level.GetDirectSignalTo(position);

    public bool HasSignal(Vector position, BlockFace direction) => level.HasSignal(position, direction);

    public bool HasNeighborSignal(Vector position) => level.HasNeighborSignal(position);

    public int GetBestNeighborSignal(Vector position) => level.GetBestNeighborSignal(position);

    public bool SetBlock(Vector position, IBlock block, BlockUpdateFlags flags = BlockUpdateFlags.All) => false;

    public bool RemoveBlock(Vector position) => false;

    public bool DestroyBlock(Vector position, bool dropItems, IEntity? breaker = null) => false;

    public bool SpawnItem(VectorD position, API.Inventory.ItemStack stack) => false;

    public void UpdateNeighbors(Vector position, IBlock source)
    {
    }

    public void UpdateAnalogOutput(Vector position)
    {
    }

    // Validation may inspect blocks, but must not expose mutable container contents.
    public API.Inventory.IItemAccess? GetItemAccess(Vector position, BlockFace? side) => null;
}
