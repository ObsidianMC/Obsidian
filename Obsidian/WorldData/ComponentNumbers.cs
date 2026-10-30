using Obsidian.API.Data;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

/// <summary>Evaluates fuel and compost components through the shared 26.3 context providers.</summary>
internal static class ComponentNumbers
{
    public static int Resolve(ComponentIntValue value, IBlock block, Random random)
    {
        if (value.Provider is not string provider)
        {
            return value.Constant;
        }

        var id = Qualify(provider);
        if (!ContextNumberProviders.Ints.TryGetValue(id, out var expression))
        {
            throw new KeyNotFoundException($"Unknown context provider '{id}'.");
        }

        return expression.GetInt(CreateContext(block, random).Numbers);
    }

    public static float Resolve(ComponentFloatValue value, IBlock block, Random random)
    {
        if (value.Provider is not string provider)
        {
            return value.Constant;
        }

        var id = Qualify(provider);
        if (!ContextNumberProviders.Floats.TryGetValue(id, out var expression))
        {
            throw new KeyNotFoundException($"Unknown context provider '{id}'.");
        }

        return expression.GetFloat(CreateContext(block, random).Numbers);
    }

    private static string Qualify(string id) => id.Contains(':') ? id : "minecraft:" + id;

    private static LootContext CreateContext(IBlock block, Random random) => new()
    {
        // Component callers own this System.Random stream. Do not draw a seed for a separate vanilla RNG.
        Random = new SystemRandomSource(random),
        BlockState = block,
        BlockProperties = block.GetProperties()
    };
}
