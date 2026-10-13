using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;

namespace Obsidian.WorldData;

/// <summary>
/// What a destroyed block drops, by vanilla's rules: harvest eligibility, the block's loot table and the loot context.
/// The drops are evaluated here and returned; spawning them and removing the block belong to the caller. Call before the
/// block is removed, so the block entity and state are still there.
/// </summary>
internal static class BlockDrops
{
    // The neighbourhood that location checks read is fetched up front: the level's block reads are async.
    private static readonly int[] PrefetchedOffsetsY = [-1, 0, 1];

    /// <summary>
    /// The stacks <paramref name="block"/> drops when a player breaks it at <paramref name="location"/> with
    /// <paramref name="tool"/> (an empty stack for bare hands). Empty when doTileDrops is off, the tool can't harvest the
    /// block, or the block has no loot table.
    /// </summary>
    public static async ValueTask<List<ItemStack>> ForPlayerBreakAsync(AbstractLevel level, IBlock block, VectorD location, ItemStack tool, IEntity? breaker)
    {
        if (block.IsAir || !level.LevelData.GetBooleanRule("do_tile_drops") || !CanHarvest(block, tool))
            return [];

        return await EvaluateAsync(level, block, location, tool, breaker, explosionRadius: null, await BlockEntityAt(level, location));
    }

    /// <summary>
    /// The stacks <paramref name="block"/> drops when an explosion of <paramref name="radius"/> destroys it. Explosions
    /// ignore tools. With <paramref name="decays"/> false the explosion radius isn't passed, so no survival check or
    /// decay applies, as with vanilla's non-decaying explosions.
    /// </summary>
    public static ValueTask<List<ItemStack>> ForExplosionAsync(AbstractLevel level, IBlock block, VectorD position, float radius, bool decays)
    {
        if (block.IsAir)
            return ValueTask.FromResult(new List<ItemStack>());

        return EvaluateDecayAsync(level, block, position, radius, decays);
    }

    private static async ValueTask<List<ItemStack>> EvaluateDecayAsync(AbstractLevel level, IBlock block, VectorD position, float radius, bool decays) =>
        await EvaluateAsync(level, block, position, tool: null, breaker: null, explosionRadius: decays ? radius : null, await BlockEntityAt(level, position));

    /// <summary>
    /// The stacks a block drops when a fluid or a lost support destroys it (vanilla <c>Block.dropResources</c> with an empty
    /// tool and no breaker). <paramref name="blockEntity"/> is the block entity as it was when the block was destroyed.
    /// Unlike breaking, this doesn't check doTileDrops, as vanilla's <c>getDrops</c> doesn't.
    /// </summary>
    public static ValueTask<List<ItemStack>> ForDestroyedAsync(AbstractLevel level, IBlock block, Vector position, IBlockEntity? blockEntity, IEntity? cause = null)
    {
        if (block.IsAir)
            return ValueTask.FromResult(new List<ItemStack>());

        return EvaluateAsync(level, block, new VectorD(position.X, position.Y, position.Z), ItemStack.Air, breaker: cause, explosionRadius: null, blockEntity);
    }

    /// <summary>
    /// Spawns the drops of the blocks fluids and lost supports destroyed during the fluid tick.
    /// </summary>
    public static async ValueTask SpawnDestroyedByFluidsAsync(AbstractLevel level, Func<int> nextEntityId)
    {
        foreach (var destroyed in level.Fluids.TakeDestroyedBlocks())
        {
            foreach (var stack in await ForDestroyedAsync(level, destroyed.Block, destroyed.Position, destroyed.BlockEntity))
                SpawnDrop(level, nextEntityId, destroyed.Position, stack);
        }

        foreach (var removed in level.Fluids.TakeRemovedContainers())
            SpillContainer(level, nextEntityId, removed.Position, removed.BlockEntity);
    }

    /// <summary>
    /// Spills the stacks of a removed container block entity at <paramref name="position"/> (see
    /// <see cref="BlockEntityComponents.SpilledContents"/>).
    /// </summary>
    public static void SpillContainer(ILevel level, Func<int> nextEntityId, Vector position, IBlockEntity? blockEntity)
    {
        foreach (var stack in BlockEntityComponents.SpilledContents(blockEntity))
        {
            if (!stack.IsAir && stack.Count > 0)
                SpawnDrop(level, nextEntityId, position, stack);
        }
    }

    private static ValueTask<IBlockEntity?> BlockEntityAt(AbstractLevel level, VectorD location) =>
        level.GetBlockEntityAsync(new Vector((int)Math.Floor(location.X), (int)Math.Floor(location.Y), (int)Math.Floor(location.Z)));

    private static async ValueTask<List<ItemStack>> EvaluateAsync(AbstractLevel level, IBlock block, VectorD location, ItemStack? tool, IEntity? breaker, float? explosionRadius, IBlockEntity? blockEntity)
    {
        var tableId = "minecraft:blocks/" + block.UnlocalizedName[(block.UnlocalizedName.IndexOf(':') + 1)..];
        if (!LootTables.All.TryGetValue(tableId, out var table))
            return [];

        var x = (int)Math.Floor(location.X);
        var y = (int)Math.Floor(location.Y);
        var z = (int)Math.Floor(location.Z);
        var neighbours = new Dictionary<(int, int, int), IBlock?>();
        foreach (var dy in PrefetchedOffsetsY)
            neighbours[(x, y + dy, z)] = await level.GetBlockAsync(x, y + dy, z);

        var context = new LootContext
        {
            Random = table.CreateRandom(0, LevelLoot.Sequences(level)),
            Origin = new VectorD(x + 0.5, y + 0.5, z + 0.5),
            ThisEntity = breaker,
            Block = block,
            Tool = tool,
            ExplosionRadius = explosionRadius,
            BlockProperty = (candidate, name) => candidate.GetProperty(name),
            BlockAt = (bx, by, bz) => neighbours.TryGetValue((bx, by, bz), out var found)
                ? found
                : throw new InvalidOperationException($"Block {bx},{by},{bz} wasn't prefetched for loot."),
            BlockEntityComponent = BlockEntityComponents.For(blockEntity),
        };

        try
        {
            return table.GetRandomItems(context).Where(stack => stack.Count > 0).ToList();
        }
        catch (NotSupportedException exception)
        {
            level.ReportUnsupportedLoot(block, exception);
            return [];
        }
    }

    /// <summary>
    /// Spawns a dropped stack above the block at <paramref name="block"/> with a small random toss, the way vanilla pops
    /// block drops. Uses the global random, so loot draws aren't affected.
    /// </summary>
    public static void SpawnDrop(ILevel level, Func<int> nextEntityId, Vector block, ItemStack stack)
    {
        var item = new ItemEntity
        {
            EntityId = nextEntityId(),
            Item = stack,
            Level = level,
            Position = (VectorD)block + 0.5,
        };

        level.TryAddEntity(item);

        var power = Globals.Random.NextSingle() * 0.1f;
        var direction = Globals.Random.NextSingle() * 6.2f;
        item.SpawnEntity(new Velocity(-Math.Sin(direction) * power, 0.2f, Math.Cos(direction) * power));
    }

    /// <summary>
    /// Whether the tool can make <paramref name="block"/> drop its loot. Blocks that don't require a correct tool always
    /// can; the others need the first tool rule naming the block to say <c>correct_for_drops</c>, like vanilla's
    /// <c>Tool.isCorrectForDrops</c>.
    /// </summary>
    public static bool CanHarvest(IBlock block, ItemStack tool)
    {
        if (!BlockPhysics.RequiresCorrectTool(block.StateId()))
            return true;

        if (EffectiveTool(tool) is not { } data)
            return false;

        foreach (var rule in data.Rules)
        {
            if (rule.CorrectDropForBlocks is { } correct && Contains(rule.Blocks, block))
                return correct;
        }

        return false;
    }

    /// <summary>
    /// The tool component in effect: a patched component wins, a removed one is absent, otherwise the item's default.
    /// </summary>
    internal static ToolDataComponent? EffectiveTool(ItemStack tool)
    {
        if (tool.GetComponent<ToolDataComponent>(DataComponentType.Tool) is { } patched)
            return patched;

        return tool.RemoveComponents.Contains(DataComponentType.Tool) ? null : ItemToolDefaults.Get(tool.Holder.Id);
    }

    private static bool Contains(IdSet set, IBlock block)
    {
        if (set.TagName is { } tagName)
        {
            var tag = BlockTags.Find(tagName) ?? throw new InvalidOperationException($"Unknown block tag {tagName} in a tool rule.");
            return tag.Entries.Contains(block.RegistryId);
        }

        return set.Ids?.Contains(block.RegistryId) ?? false;
    }
}
