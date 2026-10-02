namespace Obsidian.WorldData.Generators;

/// <summary>
/// The build range the non-vanilla generators (superflat, empty, island and the legacy overworld) are written for.
/// </summary>
internal static class OverworldBuildRange
{
    public const int MinY = -64;

    public const int Height = 384;

    /// <summary>
    /// Rejects chunks with another build range, since these generators only produce overworld-sized chunks.
    /// </summary>
    public static void Ensure(IChunk? chunk, string generatorId)
    {
        if (chunk is not null && (chunk.MinY != MinY || chunk.Height != Height))
            throw new NotSupportedException(
                $"The '{generatorId}' generator only supports the overworld build range (min Y {MinY}, height {Height}), not {chunk.MinY}/{chunk.Height}.");
    }
}
