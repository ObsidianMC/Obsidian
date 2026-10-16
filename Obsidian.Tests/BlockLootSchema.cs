using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.Loot.Entries;
using Obsidian.API.Registries;
using Obsidian.Registries;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Checks that vanilla 1.21.11's block loot tables are imported and compiled. Evaluation parity is tested separately.
/// </summary>
public class BlockLootSchema
{
    [Fact]
    public void AllVanillaBlockTablesAreLoaded()
    {
        var blockTables = LootTables.All.Keys.Where(id => id.StartsWith("minecraft:blocks/", StringComparison.Ordinal)).ToList();

        Assert.Equal(1083, blockTables.Count);
        Assert.Contains("minecraft:blocks/stone", blockTables);
        Assert.Contains("minecraft:blocks/acacia_leaves", blockTables);
        Assert.Equal("minecraft:blocks/stone", LootTables.Blocks.Stone.Identifier);
        Assert.Equal("minecraft:block", LootTables.Blocks.Stone.Type);
    }

    [Fact]
    public void AlternativesEntriesAreCompiled()
    {
        var entries = LootTables.Blocks.AcaciaLeaves.Pools.SelectMany(pool => pool.Entries).ToList();

        Assert.Contains(entries, entry => entry is AlternativesEntry);
    }

    [Fact]
    public void CrackedPotSherdsNeedTheBlockEntityAndFailWithoutIt()
    {
        // minecraft:sherds reads the pot's saved sides; without a block entity it must fail, not drop defaults.
        var table = LootTables.Blocks.DecoratedPot;
        var context = new LootContext
        {
            Random = table.CreateRandom(1L),
            Origin = new VectorD(0.5, 64.5, 0.5),
            Block = BlockStateProperties.GetState("minecraft:decorated_pot", new Dictionary<string, string> { ["cracked"] = "true" }),
            Tool = ItemStack.Air,
            BlockProperty = (block, name) => block.GetProperty(name),
        };

        Assert.Throws<NotSupportedException>(() => table.GetRandomItems(context));
    }

    [Fact]
    public void IntactPotDropsItselfWithoutABlockEntity()
    {
        var table = LootTables.Blocks.DecoratedPot;
        var context = new LootContext
        {
            Random = table.CreateRandom(1L),
            Origin = new VectorD(0.5, 64.5, 0.5),
            Block = BlockStateProperties.GetState("minecraft:decorated_pot", new Dictionary<string, string> { ["cracked"] = "false" }),
            Tool = ItemStack.Air,
            BlockProperty = (block, name) => block.GetProperty(name),
        };

        var stacks = table.GetRandomItems(context);

        Assert.Equal(ItemsRegistry.DecoratedPot.Id, Assert.Single(stacks).Holder.Id);
    }
}
