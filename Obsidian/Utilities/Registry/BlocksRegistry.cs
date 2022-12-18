using System.Linq.Expressions;

namespace Obsidian.Utilities.Registry;

internal partial class BlocksRegistry
{
    //Maybe we should make this a temp cache?
    private static readonly ConcurrentDictionary<string, Func<IBlock>> blockCache = new();

    static BlocksRegistry()
    {
        //Lets cache everything first
        foreach (var resourceId in ResourceIds)
        {
            Get(resourceId);
        }
    }

    public static IBlock Get(int stateId)
    {
        var baseId = StateToBase[stateId];
        var registryId = StateToNumeric[baseId];
        var blockName = Names[registryId];

        if (blockName.EndsWith("Button"))
            blockName = "ButtonBlock";
        else if (blockName == "Obsidian")
            blockName += "Block";

        if (blockCache.TryGetValue(blockName, out var value))
            return value() ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.Blocks.{blockName}");

        var ctor = type!.GetConstructor(Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var conversion = Expression.Convert(expression, typeof(IBlock));
        var lambda = Expression.Lambda<Func<IBlock>>(conversion);

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(blockName, compiledLamdba);

        return compiledLamdba();
    }

    public static int GetNetworkId(int stateId)
    {
        var baseId = StateToBase[stateId];

        return StateToNumeric[baseId];
    }

    public static IBlock Get(string resourceId)
    {
        if (!ResourceIds.Contains(resourceId))
            throw new InvalidOperationException($"{resourceId} is not a valid block.");

        var index = Array.IndexOf(ResourceIds, resourceId);

        var blockName = Names[index];

        if (blockName.EndsWith("Button"))
            blockName = "ButtonBlock";
        else if (blockName == "Obsidian")
            blockName += "Block";

        if (blockCache.TryGetValue(blockName, out var value))
            return value() ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.Blocks.{blockName}");

        var ctor = type!.GetConstructor(Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var conversion = Expression.Convert(expression, typeof(IBlock));
        var lambda = Expression.Lambda<Func<IBlock>>(conversion);

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(blockName, compiledLamdba);

        return compiledLamdba();
    }

    public static IBlock Get(Material material, IStateBuilder<IBlock> stateBuilder = null)
    {
        var materialString = material.ToString();
        if (!Names.Contains(materialString))
            throw new InvalidOperationException($"{material} is not a valid block.");

        if (materialString == "Button")
            materialString += "Block";

        if (blockCache.TryGetValue(materialString, out var value))
            return value() ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.Blocks.{materialString}");

        var ctor = type!.GetConstructor(Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var conversion = Expression.Convert(expression, typeof(IBlock));
        var lambda = Expression.Lambda<Func<IBlock>>(conversion);

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(materialString, compiledLamdba);

        return compiledLamdba();
    }
}
