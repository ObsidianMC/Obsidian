using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;

namespace Obsidian.Registries;

internal static partial class BlockLoot
{
    /// <summary>
    /// Captures locations before block removal, then evaluates without blocking an asynchronous world read.
    /// </summary>
    public static async ValueTask<List<ItemStack>> GetDropsAsync(
        ILevel level,
        Vector position,
        IBlock block,
        ItemStack? tool,
        IRandomSource? random = null,
        IEntity? breaker = null,
        float? explosionRadius = null,
        bool checkTool = true,
        IReadOnlyList<DataComponent>? blockEntityComponents = null,
        Action<string, Action<ItemStack>>? dynamicDrops = null)
    {
        var snapshot = Capture(block);
        random ??= (snapshot.Table ?? LootTable.Empty).CreateRandom(0, SequencesFor(level));
        var locations = new Dictionary<Vector, IBlock?>();
        var visited = new HashSet<LootTable>();
        var pending = new Queue<LootTable>();
        if (snapshot.Table is LootTable table)
            pending.Enqueue(table);

        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current))
                continue;

            foreach (var condition in current.LocationChecks)
            {
                var target = Offset(position, condition);
                if (!locations.ContainsKey(target))
                    locations[target] = await level.GetBlockAsync(target);
            }

            foreach (var reference in current.References)
            {
                if (snapshot.Resolve(reference) is LootTable nested)
                    pending.Enqueue(nested);
            }
        }

        return Evaluate(snapshot, block, tool, random, breaker, explosionRadius, (VectorD)position + 0.5,
            condition => TestLocation(condition, locations.GetValueOrDefault(Offset(position, condition))),
            checkTool, blockEntityComponents, dynamicDrops);
    }

    /// <summary>
    /// The loot of a block destroyed without a tool, like vanilla's <c>Block.dropResources</c>, for a caller that
    /// reads the level's blocks synchronously (under the level's tick lock) through <paramref name="getBlock"/>.
    /// </summary>
    public static List<ItemStack> GetDrops(ILevel level, Vector position, IBlock block, Func<Vector, IBlock> getBlock)
    {
        var snapshot = Capture(block);
        var random = (snapshot.Table ?? LootTable.Empty).CreateRandom(0, SequencesFor(level));

        return Evaluate(snapshot, block, null, random, null, null, (VectorD)position + 0.5,
            condition => TestLocation(condition, getBlock(Offset(position, condition))),
            checkTool: false, blockEntityComponents: null, dynamicDrops: null);
    }

    private static Vector Offset(Vector position, JsonElement condition) => position + new Vector(
        condition.TryGetProperty("offsetX", out var x) ? x.GetInt32() : 0,
        condition.TryGetProperty("offsetY", out var y) ? y.GetInt32() : 0,
        condition.TryGetProperty("offsetZ", out var z) ? z.GetInt32() : 0);

    private static bool TestLocation(JsonElement condition, IBlock? block)
    {
        if (block is null)
            return false;

        var predicate = condition.GetProperty("predicate");
        if (!predicate.TryGetProperty("block", out var blockPredicate))
            return !predicate.EnumerateObject().Any();

        return (!blockPredicate.TryGetProperty("blocks", out var blocks)
                || LootTableJson.MatchesRegistrySet(blocks, "block", block.UnlocalizedName, block.RegistryId))
            && (!blockPredicate.TryGetProperty("state", out var state)
                || LootTableJson.MatchesProperties(block.GetProperties(), state));
    }
}
