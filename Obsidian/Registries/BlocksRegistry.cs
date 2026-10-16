using Obsidian.API.World.Features.Trees;
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

        // Blocks without properties have one state, which is their default block.
        stateBlocks = new IBlock[StateToNumeric.Length];
        for (int stateId = 0; stateId < stateBlocks.Length; stateId++)
        {
            var registryId = StateToNumeric[stateId];
            stateBlocks[stateId] = CreateState(registryId, stateId) ?? defaultBlocks[registryId];
        }

        plugins = new PluginBlockTable([], StateToNumeric.Length);

        SimpleBlockStateExtensions.SetConverter(GetFromSimpleState);
        TreeBlockRules.States = new BlockStateAccess();
    }

    /// <summary>
    /// The block state with the given id: a vanilla state, a plugin block's, or air for an id no state has.
    /// </summary>
    public static IBlock Get(int stateId)
    {
        if ((uint)stateId < (uint)stateBlocks.Length)
            return stateBlocks[stateId];

        return TryGetPluginBlock(stateId, out var type) ? type.StateOf(stateId) : Air;
    }

    /// <summary>The registry id of the block a state belongs to.</summary>
    public static int RegistryIdOf(int stateId)
    {
        if ((uint)stateId < (uint)StateToNumeric.Length)
            return StateToNumeric[stateId];

        return TryGetPluginBlock(stateId, out var type) ? type.Id : 0;
    }

    /// <summary>
    /// The default state of the block with the given id, like <see cref="BlockIds.Stone"/>, or a plugin's block's.
    /// </summary>
    public static IBlock GetDefault(int blockId)
    {
        if ((uint)blockId < (uint)defaultBlocks.Length)
            return defaultBlocks[blockId];

        return PluginBlocks.FirstOrDefault(type => type.Id == blockId)?.DefaultState
            ?? throw new ArgumentOutOfRangeException(nameof(blockId), blockId, "There's no block with this id.");
    }

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

        if (resourceIdToName.TryGetValue(resourceId, out var blockName))
            return defaultBlockCache[blockName];

        return TryGetPluginBlock(resourceId, out var type)
            ? type.DefaultState
            : throw new InvalidOperationException($"{resourceId} is not a valid block.");
    }
}
