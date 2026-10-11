using Obsidian.API.World.Generator;
using Obsidian.Providers.BlockStateProviders;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Registries;

internal static partial class BlocksRegistry
{
    // Every block, built once from the generated factories: block reads are on generation's hottest paths, so they're
    // array lookups.
    private static readonly IBlock[] defaultBlocks;
    private static readonly IBlock[] stateBlocks;
    private static readonly IBlock?[] materialBlocks;
    private static readonly Material[] registryMaterials;

    public static int GlobalBitsPerBlocks { get; private set; }

    [SuppressMessage("Performance", "CA1810", Justification = "Reads the generated tables (Names, StateToNumeric) of other parts of this class, and the order of initializers across parts is unspecified.")]
    static BlocksRegistry()
    {
        defaultBlocks = new IBlock[Names.Length];
        for (int i = 0; i < Names.Length; i++)
        {
            resourceIdToName.TryAdd(ResourceIds[i], Names[i]);
            defaultBlocks[i] = CreateDefault(i);
            defaultBlockCache.TryAdd(Names[i], defaultBlocks[i]);
        }

        registryMaterials = [.. defaultBlocks.Select(block => block.Material)];

        // Materials name items too, so only some have a block.
        materialBlocks = new IBlock?[Enum.GetValues<Material>().Max(material => (int)material) + 1];
        for (int i = 0; i < Names.Length; i++)
        {
            if (Enum.TryParse<Material>(Names[i], out var material))
                materialBlocks[(int)material] = defaultBlocks[i];
        }

        // Blocks without properties have one state, which is their default block.
        stateBlocks = new IBlock[StateToNumeric.Length];
        for (int stateId = 0; stateId < stateBlocks.Length; stateId++)
        {
            var registryId = StateToNumeric[stateId];
            stateBlocks[stateId] = CreateState(registryId, stateId) ?? defaultBlocks[registryId];
        }

        GlobalBitsPerBlocks = (int)Math.Ceiling(Math.Log2(StateToBase.Length));

        SimpleBlockStateExtensions.SetConverter(GetFromSimpleState);
    }

    public static IBlock Get(int stateId) => stateBlocks[stateId];

    /// <summary>The registry id of the block a state belongs to.</summary>
    public static int RegistryIdOf(int stateId) => StateToNumeric[stateId];

    /// <summary>The material of the block a state belongs to.</summary>
    public static Material MaterialOf(int stateId) => registryMaterials[StateToNumeric[stateId]];

    public static string? GetBlockName(string resourceId) => resourceIdToName.GetValueOrDefault(resourceId);

    /// <summary>
    /// Gets the exact block state described by a name and properties (unspecified properties use their defaults).
    /// </summary>
    public static IBlock GetFromSimpleState(SimpleBlockState simpleState) =>
        BlockStateProperties.GetState(simpleState.Name, simpleState.Properties);

    public static IBlock Get(string resourceId, IBlockState? state = null)
    {
        if (state is not null)
            return Get(state.Id);

        if (!resourceIdToName.TryGetValue(resourceId, out var blockName))
            throw new InvalidOperationException($"{resourceId} is not a valid block.");

        return defaultBlockCache[blockName];
    }

    public static IBlock Get(Material material, IBlockState? state = null)
    {
        if (state is not null)
            return Get(state.Id);

        return (uint)material < (uint)materialBlocks.Length && materialBlocks[(int)material] is IBlock block
            ? block
            : throw new InvalidOperationException($"{material} is not a valid block.");
    }
}
