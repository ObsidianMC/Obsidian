namespace Obsidian.WorldData;

/// <summary>
/// Block update logic for blocks that react to their neighbors. Fluids run through their scheduled ticks instead (see
/// <see cref="Fluids.LevelFluids"/>).
/// </summary>
internal static class BlockUpdates
{
    /// <summary>
    /// Perform gravity affected block tick logic
    /// </summary>
    /// <param name="level">The level of the update, which spawns the falling block</param>
    /// <param name="blockUpdate">Info about the block update</param>
    /// <returns>Whether caller should block update neighbors</returns>
    internal static async Task<bool> HandleFallingBlock(AbstractLevel level, IBlockUpdate blockUpdate)
    {
        // Only vanilla's blocks fall.
        if (blockUpdate.Block is not IBlock block || BlocksRegistry.TryGetPluginBlock(block.GetHashCode(), out _))
            return false;

        var position = blockUpdate.Position;
        if (await level.GetBlockAsync(position + Vector.Down) is IBlock below &&
            below.IsFreeForFallingBlock())
        {
            await level.SetBlockAsync(position, BlocksRegistry.Air);
            level.SpawnFallingBlock(position, BlocksRegistry.GetDefault(block.RegistryId));
            return true;
        }

        return false;
    }
}
