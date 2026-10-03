using Obsidian.API.Loot;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Maps;
using System.Runtime.CompilerServices;

namespace Obsidian.Events;

public sealed partial class MainEventHandler
{
    // Vanilla shares one set of random sequences between a server's levels and saves it; these live per level, in memory.
    private static readonly ConditionalWeakTable<ILevel, RandomSequences> randomSequences = [];

    /// <summary>
    /// Fills <paramref name="container"/> from the loot table world generation stored in <paramref name="blockEntity"/>,
    /// like vanilla's <c>RandomizableContainer.unpackLootTable</c> when <paramref name="player"/> opens the container.
    /// Does nothing when there's no loot table.
    /// </summary>
    /// <remarks>
    /// The caller replaces the block entity with the filled container, which also drops the loot table so it isn't
    /// generated again.
    /// </remarks>
    private static void UnpackLootTable(DataBlockEntity blockEntity, BaseContainer container, Player player)
    {
        if (!blockEntity.Data.TryGetTag<NbtTag<string>>("LootTable", out var lootTable))
            return;

        var seed = blockEntity.Data.TryGetTag<NbtTag<long>>("LootTableSeed", out var lootTableSeed) ? lootTableSeed.Value : 0L;

        // Vanilla generates nothing for unknown tables.
        var table = LootTables.All.GetValueOrDefault(lootTable.Value!, LootTable.Empty);
        var sequences = randomSequences.GetValue(player.Level, level => new RandomSequences(RandomState.ParseSeed(level.Seed)));
        var position = blockEntity.BlockPosition;

        // Players have no luck attribute yet, so their luck is vanilla's default of 0.
        table.Fill(container, new LootContext
        {
            Random = table.CreateRandom(seed, sequences),
            Origin = new VectorF(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f),
            ThisEntity = player,
            ExplorationMaps = ExplorationMaps.For(player.Level)
        });
    }
}
