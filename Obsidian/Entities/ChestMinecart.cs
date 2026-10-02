using Obsidian.Nbt;

namespace Obsidian.Entities;

/// <summary>
/// A minecart with a chest, like the ones world generation leaves in mineshafts.
/// </summary>
/// <remarks>
/// Obsidian can't open minecarts yet, so only the loot table the chest is waiting for is modeled; saved items are kept
/// as unmodeled data.
/// </remarks>
[MinecraftEntity("minecraft:chest_minecart")]
public sealed partial class ChestMinecart : Entity
{
    /// <summary>
    /// The loot table that fills the chest when it's first opened, or <c>null</c> once it's been filled.
    /// </summary>
    public string? LootTable { get; set; }

    /// <summary>
    /// The seed of <see cref="LootTable"/>; <c>0</c> means a random one.
    /// </summary>
    public long LootTableSeed { get; set; }

    public ChestMinecart() => this.Type = EntityType.ChestMinecart;

    // Vanilla's RandomizableContainer.trySaveLootTable: the seed is only saved with a table, and only when it's set.
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.SetOrRemove("LootTable", this.LootTable is null ? null : new NbtTag<string>("LootTable", this.LootTable));
        tag.SetOrRemove("LootTableSeed", this.LootTable is null || this.LootTableSeed == 0 ? null : new NbtTag<long>("LootTableSeed", this.LootTableSeed));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        this.LootTable = tag.TryGetTag<NbtTag<string>>("LootTable", out var lootTable) ? lootTable.Value : null;
        this.LootTableSeed = tag.TryGetTag<NbtTag<long>>("LootTableSeed", out var seed) ? seed.Value : 0;
    }
}
