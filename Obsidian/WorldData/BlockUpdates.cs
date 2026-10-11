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
    /// <param name="blockUpdate">Info about the block update</param>
    /// <returns>Whether caller should block update neighbors</returns>
    internal static async Task<bool> HandleFallingBlock(IBlockUpdate blockUpdate)
    {
        // Only vanilla's blocks fall.
        if (blockUpdate.Block?.Material is not Material material) { return false; }

        var world = blockUpdate.Level;
        var position = blockUpdate.Position;
        if (await world.GetBlockAsync(position + Vector.Down) is IBlock below &&
            below.IsFreeForFallingBlock())
        {
            await world.SetBlockAsync(position, BlocksRegistry.Air);
            world.SpawnFallingBlock(position, material);
            return true;
        }

        return false;
    }
}
