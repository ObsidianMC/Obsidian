using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.World;
using Obsidian.Nbt;

namespace EngineSample.TickCounter;

/// <summary>
/// The tick counter's block entity: counts the ticks its block is powered, saves the count with its chunk and sends it
/// to the players watching it once a second. It also gives the block its shape, and its count to its block's item when
/// the block breaks, which it counts on from once placed again.
/// </summary>
public sealed class TickCounterBlockEntity : BlockEntity
{
    private const int TicksPerSecond = 20;

    // 12 pixels tall, for both collision and the outline players aim at.
    private static readonly BlockShapes shape = new(
        [new BoundingBox(new VectorD(0, 0, 0), new VectorD(1, 0.75, 1))],
        [new BoundingBox(new VectorD(0, 0, 0), new VectorD(1, 0.75, 1))]);

    // Changed on the level's tick and read by the block's comparator hook, which may run on another thread: an int is
    // written whole.
    private int ticks;

    /// <summary>
    /// The ticks counted while powered.
    /// </summary>
    public int Ticks => this.ticks;

    /// <summary>
    /// What comparators read from the block: the whole seconds counted, up to 15.
    /// </summary>
    public int ComparatorOutput => Math.Min(15, this.ticks / TicksPerSecond);

    protected override void Load(NbtCompound tag) =>
        this.ticks = tag.TryGetTagValue<int>("ticks", out var ticks) ? ticks : 0;

    protected override void Save(NbtCompound tag) => tag.Add(new NbtTag<int>("ticks", this.ticks));

    // What clients get, with the chunk and from SendUpdate: a client mod could show the count.
    protected override NbtCompound? GetUpdateData() => new() { new NbtTag<int>("ticks", this.ticks) };

    // The same box whatever the data: a client mod gives its block the same shape.
    protected override BlockShapes? GetShapes() => shape;

    // A counter that never counted leaves its item bare, so that it stacks with new ones.
    protected override void CollectComponents(ItemStack item)
    {
        if (this.ticks > 0)
            item.Set(TickCounterBlock.Ticks, this.ticks);
    }

    protected override void ApplyComponents(ItemStack item)
    {
        if (item.TryGet(TickCounterBlock.Ticks, out var ticks))
            this.ticks = ticks;
    }

    protected override async ValueTask TickAsync()
    {
        if (this.Level is not ILevel level || await level.GetBlockAsync(this.BlockPosition) is not IBlock block)
            return;

        if (!TickCounterBlock.IsPowered(block))
            return;

        this.ticks++;

        // Saved at the end of the tick, when the comparators reading the block hear of the change too.
        this.SetChanged();

        if (this.ticks % TicksPerSecond == 0)
        {
            this.SendUpdate();

            // Its analog output (the seconds, up to 15) changes only now: the comparators beside it hear of that.
            level.UpdateNeighbors(this.BlockPosition, block);
        }
    }
}
