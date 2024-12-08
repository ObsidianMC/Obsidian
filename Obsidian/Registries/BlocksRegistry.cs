using System.Linq.Expressions;

namespace Obsidian.Registries;

internal static partial class BlocksRegistry
{
    private static string[] illegalBlockNames = ["Obsidian", "TrialSpawner", "Vault"];

    public static int GlobalBitsPerBlocks { get; private set; }
    static BlocksRegistry()
    {
        //Lets cache everything first
        for (int i = 0; i < ResourceIds.Length; i++)
        {
            resourceIdToName.TryAdd(ResourceIds[i], Names[i]);
        }

        foreach (var resourceId in ResourceIds)
        {
            Get(resourceId);
        }

        for (int i = 0; i < AllStates.Length; i++)
        {
            Get(AllStates[i]);
        }

        GlobalBitsPerBlocks = (int)Math.Ceiling(Math.Log2(StateToBase.Length));
    }

    public static IBlock Get(int stateId)
    {
        if (blockWithStateCache.TryGetValue(stateId, out var value))
            return value;

        var registryId = StateToNumeric[stateId];
        var blockName = Names[registryId];
        var resourceId = ResourceIds[registryId];

        if(!blockTypeCache.TryGetValue(blockName, out var type))
        {
            var sanitizedBlockName = GetSanitizedName(blockName);

            type = Type.GetType($"Obsidian.Blocks.{sanitizedBlockName}");

            blockTypeCache.TryAdd(blockName, type);
        }

        var ctorWithState = type!.GetConstructor(stateIdParameters);

        if (ctorWithState is null)
            return Get(resourceId);

        var expressionWithState = Expression.New(ctorWithState, stateIdParameterExpressions);

        var conversionWithState = Expression.Convert(expressionWithState, blockType);
        var lambdaWithState = Expression.Lambda<Func<int, IBlock>>(conversionWithState, stateIdParameterExpressions);

        var compiledLamdbaWithState = lambdaWithState.Compile();

        var block = compiledLamdbaWithState(stateId);

        blockWithStateCache.TryAdd(stateId, block);

        return block;
    }

    public static IBlock Get(string resourceId, IBlockState? state = null)
    {
        if (state != null)
            return Get(state.Id);

        if (!resourceIdToName.TryGetValue(resourceId, out var blockName))
            throw new InvalidOperationException($"{resourceId} is not a valid block.");

        if (defaultBlockCache.TryGetValue(blockName, out var value))
            return value;

        if (!blockTypeCache.TryGetValue(blockName, out var type))
        {
            var sanitizedBlockName = GetSanitizedName(blockName);

            type = Type.GetType($"Obsidian.Blocks.{sanitizedBlockName}");

            blockTypeCache.TryAdd(blockName, type!);
        }

        var ctor = type!.GetConstructor(Type.EmptyTypes)!;

        var expression = Expression.New(ctor);

        var conversion = Expression.Convert(expression, blockType);
        var lambda = Expression.Lambda<Func<IBlock>>(conversion);

        var compiledLamdba = lambda.Compile();

        var block = compiledLamdba();

        defaultBlockCache.TryAdd(blockName, block);

        return block;
    }

    public static IBlock Get(Material material, IBlockState? state = null)
    {
        if (state != null)
            return Get(state.Id);

        var materialString = material.ToString();

        if (defaultBlockCache.TryGetValue(materialString, out var value))
            return value;
        
        if (!Names.Contains(materialString))
            throw new InvalidOperationException($"{material} is not a valid block.");

        if (!blockTypeCache.TryGetValue(materialString, out var type))
        {
            var sanitizedBlockName = GetSanitizedName(materialString);

            type = Type.GetType($"Obsidian.Blocks.{sanitizedBlockName}");

            blockTypeCache.TryAdd(materialString, type!);
        }

        var ctor = type!.GetConstructor(Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var conversion = Expression.Convert(expression, blockType);
        var lambda = Expression.Lambda<Func<IBlock>>(conversion);

        var compiledLamdba = lambda.Compile();
        var block = compiledLamdba();

        defaultBlockCache.TryAdd(materialString, block);

        return block;
    }

    private static string GetSanitizedName(string value) =>
        illegalBlockNames.Contains(value) ? $"{value}Block" : string.Empty;
}
