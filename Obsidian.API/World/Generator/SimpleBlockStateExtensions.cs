namespace Obsidian.API.World.Generator;

/// <summary>
/// Extension methods for converting SimpleBlockState to IBlock.
/// Since ChunkBuilder is in the API project and can't access BlocksRegistry directly,
/// we use a delegate pattern to provide the conversion logic.
/// </summary>
public static class SimpleBlockStateExtensions
{
    private static Func<SimpleBlockState, IBlock>? _converter;

    /// <summary>
    /// Sets the converter function used to transform SimpleBlockState to IBlock.
    /// This should be set during initialization by the main Obsidian project.
    /// </summary>
    internal static void SetConverter(Func<SimpleBlockState, IBlock> converter)
    {
        _converter = converter;
    }

    /// <summary>
    /// Converts a SimpleBlockState to an IBlock instance.
    /// </summary>
    public static IBlock ToBlock(this SimpleBlockState state)
    {
        if (_converter == null)
            throw new InvalidOperationException(
                "SimpleBlockState converter not initialized. " +
                "Call SimpleBlockStateExtensions.SetConverter() during startup.");

        return _converter(state);
    }
}
