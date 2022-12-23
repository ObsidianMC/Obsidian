using System.Linq.Expressions;

namespace Obsidian.Utilities.Registry;

internal partial class BlocksRegistry
{
    //Maybe we should make this a temp cache?
    private static readonly ConcurrentDictionary<string, Func<IBlockState, IBlock>> blockCache = new();
    private static readonly ConcurrentDictionary<string, Func<int, IBlock>> blockWithIdCache = new();

    private static readonly Type blockType = typeof(IBlock);


    static BlocksRegistry()
    {
        //Lets cache everything first
        foreach (var resourceId in ResourceIds)
        {
            Get(resourceId);
        }

        for (int i = 0; i < AllStates.Length; i++)
        {
            Get(AllStates[i]);
        }
    }

    public static IBlock Get(int stateId)
    {
        var registryId = StateToNumeric[stateId];
        var blockName = Names[registryId];
        var resourceId = ResourceIds[registryId];

        if (blockName == "Obsidian")
            blockName += "Block";

        if (blockWithIdCache.TryGetValue(blockName, out var value))
            return value(stateId) ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.Blocks.{blockName}");

        var parameters = new[] { typeof(int) };
        var expressionParams = parameters.GetParamExpressions();

        var ctorWithState = type!.GetConstructor(parameters);

        if (ctorWithState is null)
            return Get(resourceId);

        var expressionWithState = Expression.New(ctorWithState, expressionParams);

        var conversionWithState = Expression.Convert(expressionWithState, blockType);
        var lambdaWithState = Expression.Lambda<Func<int, IBlock>>(conversionWithState, expressionParams);

        var compiledLamdbaWithState = lambdaWithState.Compile();

        blockWithIdCache.TryAdd(blockName, compiledLamdbaWithState);

        return compiledLamdbaWithState(stateId);
    }

    public static int GetNetworkId(int stateId)
    {
        var baseId = StateToBase[stateId];

        return StateToNumeric[baseId];
    }

    public static IBlock Get(string resourceId, IBlockState? state = null)
    {
        if (!ResourceIds.Contains(resourceId))
            throw new InvalidOperationException($"{resourceId} is not a valid block.");

        var index = Array.IndexOf(ResourceIds, resourceId);

        var blockName = Names[index];

        if (blockName == "Obsidian")
            blockName += "Block";

        if (blockCache.TryGetValue(blockName, out var value))
            return value(state) ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.Blocks.{blockName}");

        if (state != null)
        {
            var parameters = new[] { state!.GetType() };

            var ctorWithState = type!.GetConstructor(parameters);

            var expressionWithState = Expression.New(ctorWithState, parameters.GetParamExpressions());

            var conversionWithState = Expression.Convert(expressionWithState, blockType);
            var lambdaWithState = Expression.Lambda<Func<IBlockState, IBlock>>(conversionWithState, parameters.GetParamExpressions());

            var compiledLamdbaWithState = lambdaWithState.Compile();

            blockCache.TryAdd(blockName, compiledLamdbaWithState);

            return compiledLamdbaWithState(state);
        }

        var ctor = type!.GetConstructor(Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var conversion = Expression.Convert(expression, blockType);
        var lambda = Expression.Lambda<Func<IBlockState, IBlock>>(conversion, new[] { Expression.Parameter(typeof(IBlockState), "state")});

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(blockName, compiledLamdba);

        return compiledLamdba(state);
    }

    public static IBlock Get(Material material, IBlockState? state = null)
    {
        var materialString = material.ToString();
        if (!Names.Contains(materialString))
            throw new InvalidOperationException($"{material} is not a valid block.");

        if (materialString == "Obsidian")
            materialString += "Block";

        if (blockCache.TryGetValue(materialString, out var value))
            return value(state) ?? throw new InvalidOperationException();

        var type = Type.GetType($"Obsidian.Blocks.{materialString}");

        if(state != null)
        {
            var parameters = new[] { state!.GetType() };

            var ctorWithState = type!.GetConstructor(parameters);

            var expressionWithState = Expression.New(ctorWithState, parameters.GetParamExpressions());

            var conversionWithState = Expression.Convert(expressionWithState, blockType);
            var lambdaWithState = Expression.Lambda<Func<IBlockState, IBlock>>(conversionWithState, parameters.GetParamExpressions());

            var compiledLamdbaWithState = lambdaWithState.Compile();

            blockCache.TryAdd(materialString, compiledLamdbaWithState);

            return compiledLamdbaWithState(state);
        }

        var ctor = type!.GetConstructor(Type.EmptyTypes);

        var expression = Expression.New(ctor);

        var conversion = Expression.Convert(expression, blockType);
        var lambda = Expression.Lambda<Func<IBlockState, IBlock>>(conversion, new[] { Expression.Parameter(typeof(IBlockState), "state") });

        var compiledLamdba = lambda.Compile();

        blockCache.TryAdd(materialString, compiledLamdba);

        return compiledLamdba(state);
    }
}
