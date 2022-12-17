using System.Linq.Expressions;
using Obsidian.Blocks;

namespace Obsidian.Utilities.Registry;
internal partial class BlocksRegistry
{
    //Maybe we should make this a temp cache?
    private static ConcurrentDictionary<string, Func<IBlock>> blockCache = new();

    public readonly static IBlock Air = new AirBlock();

    public static IBlock Get(int stateId)
    {
        var baseId = NumericToBase[stateId];
        var registryId = StateToNumeric[baseId];
        var blockName = Names[registryId];
        var baseName = blockName.TrimResourceTag().ToPascalCase().Replace("Block", string.Empty);

        var typeName = $"{baseName}Block";

        if (blockCache.TryGetValue(typeName, out var value))
            return value() ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.API.Blocks.{typeName}");

        var ctor = type!.GetConstructor(Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var lambda = Expression.Lambda<Func<IBlock>>(expression);

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(typeName, compiledLamdba);

        return compiledLamdba();
    }

    public static IBlock Get(string blockName)
    {
        var baseName = blockName.TrimResourceTag().ToPascalCase().Replace("Block", string.Empty);

        var typeName = $"{baseName}Block";

        if (blockCache.TryGetValue(typeName, out var value))
            return value() ?? throw new InvalidOperationException();


        var type = Type.GetType($"Obsidian.API.Blocks.{typeName}");

        var ctor = type!.GetConstructor(Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var lambda = Expression.Lambda<Func<IBlock>>(expression);

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(typeName, compiledLamdba);

        return compiledLamdba();
    }

    public static IBlock Get(Material material, IStateBuilder<IBlock> stateBuilder = null)
    {
        var typeName = $"{material.ToString().Replace("Block", string.Empty)}Block";

        if (blockCache.TryGetValue(typeName, out var value))
            return value() ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.API.Blocks.{typeName}");

        var ctor = type!.GetConstructor(Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var lambda = Expression.Lambda<Func<IBlock>>(expression);

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(typeName, compiledLamdba);

        return compiledLamdba();
    }
}
