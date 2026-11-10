using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// Suspicious sand's or gravel's block entity (vanilla's <c>minecraft:brushable_block</c>): the loot table its item
/// comes from, or the item once rolled, the face first brushed, and how far brushing has gone (see
/// <see cref="Brushing"/>).
/// </summary>
/// <remarks>
/// The level's tick changes it under the level's tick lock; saves and client updates read it from any thread, so its
/// fields are kept under its own lock.
/// </remarks>
internal sealed class BrushableBlockEntity : BlockEntity
{
    // Vanilla's BrushableBlockEntity constants: strokes at least this far apart count, brushing stopped this long
    // starts to undo, it then undoes two strokes this often, and this many strokes finish it.
    internal const int CooldownTicks = 10;
    internal const int ResetTicks = 40;
    internal const int RetractTicks = 4;
    internal const int StrokesToFinish = 10;

    private readonly Lock state = new();

    private string? lootTable;
    private long lootTableSeed;
    private ItemStack? item;
    private BlockFace? hitDirection;
    private int brushCount;
    private long resetsAt;
    private long cooldownEndsAt;

    internal override bool SavesOnAnyThread => true;

    /// <summary>The loot table the item is still to be rolled from, if any.</summary>
    internal string? LootTable
    {
        get
        {
            lock (this.state)
            {
                return this.lootTable;
            }
        }
    }

    internal long LootTableSeed
    {
        get
        {
            lock (this.state)
            {
                return this.lootTableSeed;
            }
        }
    }

    /// <summary>The item brushing uncovers, once rolled.</summary>
    internal ItemStack? Item
    {
        get
        {
            lock (this.state)
            {
                return this.item;
            }
        }
    }

    /// <summary>The face the first counted stroke brushed, which the item shows on and pops out of.</summary>
    internal BlockFace? HitDirection
    {
        get
        {
            lock (this.state)
            {
                return this.hitDirection;
            }
        }
    }

    internal int BrushCount
    {
        get
        {
            lock (this.state)
            {
                return this.brushCount;
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>setLootTable</c>: the table the item comes from, rolled with <paramref name="seed"/> (zero for
    /// the level's random sequence) on the first stroke.
    /// </summary>
    internal void SetLootTable(string table, long seed)
    {
        lock (this.state)
        {
            this.lootTable = table;
            this.lootTableSeed = seed;
        }

        this.SetChanged();
    }

    /// <summary>
    /// Takes the rolled <paramref name="rolled"/> item in place of the loot table, if the table is still
    /// <paramref name="table"/> with <paramref name="seed"/>; otherwise someone rolled or replaced it meanwhile.
    /// </summary>
    internal bool TakeLoot(string table, long seed, ItemStack? rolled)
    {
        lock (this.state)
        {
            if (this.lootTable != table || this.lootTableSeed != seed)
                return false;

            this.item = rolled is { Count: > 0, IsAir: false } ? rolled : null;
            this.lootTable = null;
            this.lootTableSeed = 0;
        }

        this.SetChanged();
        return true;
    }

    /// <summary>
    /// Vanilla's <c>brush</c> up to its loot, which the caller rolled first: remembers the face, pushes the reset
    /// back, and counts the stroke unless it's within the cooldown of the last one.
    /// </summary>
    /// <returns>The completion state before and after, and whether the stroke counted and finished the block.</returns>
    internal (int Before, int After, bool Counted, bool Finished) Brush(long gameTime, BlockFace face)
    {
        lock (this.state)
        {
            this.hitDirection ??= face;
            this.resetsAt = gameTime + ResetTicks;
            var before = Completion(this.brushCount);
            if (gameTime < this.cooldownEndsAt)
                return (before, before, false, false);

            this.cooldownEndsAt = gameTime + CooldownTicks;
            this.brushCount++;
            return (before, Completion(this.brushCount), true, this.brushCount >= StrokesToFinish);
        }
    }

    /// <summary>
    /// Vanilla's <c>checkReset</c>: once brushing has stopped long enough, two strokes come undone every few ticks;
    /// none left forgets the face and the timers.
    /// </summary>
    /// <returns>The completion state before and after, and whether the block keeps checking.</returns>
    internal (int Before, int After, bool Pending) CheckReset(long gameTime)
    {
        lock (this.state)
        {
            var before = Completion(this.brushCount);
            if (this.brushCount != 0 && gameTime >= this.resetsAt)
            {
                this.brushCount = Math.Max(0, this.brushCount - 2);
                this.resetsAt = gameTime + RetractTicks;
            }

            var after = Completion(this.brushCount);
            if (this.brushCount != 0)
                return (before, after, true);

            this.hitDirection = null;
            this.resetsAt = 0;
            this.cooldownEndsAt = 0;
            return (before, after, false);
        }
    }

    /// <summary>Takes the item out, for the finished block to drop.</summary>
    internal ItemStack? TakeItem()
    {
        ItemStack? taken;
        lock (this.state)
        {
            taken = this.item;
            this.item = null;
        }

        this.SetChanged();
        return taken;
    }

    // Vanilla's getCompletionState: the dusted property for a number of strokes.
    internal static int Completion(int strokes) => strokes switch
    {
        0 => 0,
        < 3 => 1,
        < 6 => 2,
        _ => 3
    };

    // Vanilla's loadAdditional: a loot table, or else the item, and the face clients are sent.
    protected internal override void Load(NbtCompound tag)
    {
        lock (this.state)
        {
            this.lootTable = tag.TryGetTag<NbtTag<string>>("LootTable", out var table) ? table.Value : null;
            this.lootTableSeed = tag.TryGetNumber<long>("LootTableSeed", out var seed) ? seed : 0;
            this.item = this.lootTable is null && tag.TryGetTag<NbtCompound>("item", out var saved)
                ? saved.ItemFromNbt()
                : null;
            this.hitDirection = tag.TryGetNumber<int>("hit_direction", out var face) && face is >= 0 and < 6
                ? (BlockFace)face
                : null;
        }
    }

    // Vanilla's saveAdditional: the loot table and its non-zero seed, or else the item.
    protected internal override void Save(NbtCompound tag)
    {
        lock (this.state)
        {
            if (this.lootTable is not null)
            {
                tag.Add(new NbtTag<string>("LootTable", this.lootTable));
                if (this.lootTableSeed != 0)
                    tag.Add(new NbtTag<long>("LootTableSeed", this.lootTableSeed));

                return;
            }

            if (this.item is { IsAir: false } held)
                tag.Add(held.ToNbt("item"));
        }
    }

    // Vanilla's getUpdateTag: the face, and the item once rolled.
    protected internal override NbtCompound? GetUpdateData()
    {
        var data = new NbtCompound();
        lock (this.state)
        {
            if (this.hitDirection is BlockFace face)
                data.Add(new NbtTag<int>("hit_direction", (int)face));

            if (this.item is { IsAir: false } held)
                data.Add(held.ToNbt("item"));
        }

        return data;
    }
}
