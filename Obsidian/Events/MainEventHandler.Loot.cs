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
    // The containers filled from world generation's block entities, until the block entity is gone. Also the lock that
    // makes one player fill each.
    private static readonly ConditionalWeakTable<DataBlockEntity, BaseContainer> generatedContainers = [];

    /// <summary>
    /// Fills <paramref name="container"/> with what world generation stored in <paramref name="blockEntity"/>:
    /// its loot table when the player opens the container, like vanilla's RandomizableContainer.unpackLootTable,
    /// or the items its template placed.
    /// </summary>
    /// <remarks>
    /// The caller replaces the block entity with the filled container, which also drops the loot table so it isn't
    /// generated again.
    /// </remarks>
    private static void FillGeneratedContainer(DataBlockEntity blockEntity, BaseContainer container, Player player)
    {
        if (!blockEntity.Data.TryGetTag<NbtTag<string>>("LootTable", out var lootTable))
        {
            BlockEntityNbt.LoadContents(blockEntity.Data, container);
            return;
        }

        var seed = blockEntity.Data.TryGetTag<NbtTag<long>>("LootTableSeed", out var lootTableSeed)
            ? lootTableSeed.Value : 0L;

        // Vanilla generates nothing for unknown tables.
        var resolver = PluginLootTables.CaptureResolver();
        var table = resolver(lootTable.Value!) ?? LootTable.Empty;
        var sequences = BlockLoot.SequencesFor(player.Level);
        var position = blockEntity.BlockPosition;
        var random = table.CreateRandom(seed, sequences);

        // Container and block tables can name the same sequence. Keep the complete fill under the same source lock
        // as block destruction, including shuffling. Players currently have vanilla's default luck of zero.
        lock (random)
        {
            table.Fill(container, new LootContext
            {
                Random = random,
                Origin = new VectorD(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
                ThisEntity = player,
                ResolveTable = resolver,
                ExplorationMaps = ExplorationMaps.For(player.Level)
            });
        }
    }
}
