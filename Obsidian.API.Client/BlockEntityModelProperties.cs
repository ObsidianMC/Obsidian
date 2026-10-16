namespace Obsidian.API.Client;

/// <summary>
/// Model parts a mod's block entity type adds to its block's model from its synced data, like NeoForge's model data:
/// a cable's connections, the parts attached to its sides, facades. Register it with
/// <see cref="IClientRegistry.RegisterBlockEntityModel"/>.
/// </summary>
/// <remarks>
/// The world is meshed with the parts, so they're lit, shaded and culled like the block's own model. A model that
/// follows the block's neighbours, like a cable connecting to them, has the server put the connections in the block
/// entity's update data (or the block state) and send an update when they change: the provider sees only its block.
/// </remarks>
public sealed class BlockEntityModelProperties
{
    /// <summary>
    /// The models the provider's <see cref="BlockModelPart"/>s may name, in the block model format, like
    /// <c>examplemod:part/cable_side</c> for <c>assets/examplemod/models/part/cable_side.json</c>. The client reads
    /// them with the block models; a part naming another model is an error.
    /// </summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>
    /// What a block entity adds to its block's model, from its latest synced update data (plain NBT values: nested
    /// compounds are dictionaries, lists are lists) and its block state, or null for the block state's model alone.
    /// It runs at most once per update and block state, on a meshing thread: keep it pure and short, and don't mutate
    /// nested values. If it throws, the error is logged and the block state's model is used.
    /// </summary>
    public required Func<IReadOnlyDictionary<string, object?>, IBlock, BlockModelData?> Provider { get; init; }
}
