using Obsidian.Blocks;
using System.Linq.Expressions;
using System.Reflection;

namespace Obsidian.Utilities.Registry;
internal partial class BlocksRegistry
{
    //Maybe we should make this a temp cache?
    private static readonly ConcurrentDictionary<string, Func<IBlock>> blockCache = new();

    public static readonly IBlock Air = new AirBlock();

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

        var baseName = blockName.Replace("Block", string.Empty);

        var typeName = $"{baseName}Block";

        if (blockCache.TryGetValue(typeName, out var value))
            return value() ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.Blocks.{typeName}");

        var ctor = type!.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var lambda = Expression.Lambda<Func<IBlock>>(expression);

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(typeName, compiledLamdba);

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

        var baseName = blockName.Replace("Block", string.Empty);

        if (baseName.EndsWith("Button"))
            baseName = "Button";

        var typeName = $"{baseName}Block";

        if (blockCache.TryGetValue(typeName, out var value))
            return value() ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.Blocks.{typeName}");

        var ctor = type!.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var lambda = Expression.Lambda<Func<IBlock>>(expression);

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(typeName, compiledLamdba);

        return compiledLamdba();
    }

    public static IBlock Get(Material material, IStateBuilder<IBlock> stateBuilder = null)
    {
        var materialString = material.ToString();
        if (!Names.Contains(materialString))
            throw new InvalidOperationException($"{material} is not a valid block.");

        var typeName = $"{materialString.Replace("Block", string.Empty)}Block";

        if (blockCache.TryGetValue(typeName, out var value))
            return value() ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.Blocks.{typeName}");

        var ctor = type!.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var lambda = Expression.Lambda<Func<IBlock>>(expression);

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(typeName, compiledLamdba);

        return compiledLamdba();
    }
}
