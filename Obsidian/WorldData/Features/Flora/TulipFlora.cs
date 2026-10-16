using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

/// <summary>
/// A patch of tulips of one colour, picked at random.
/// </summary>
public class TulipFlora(GenHelper helper, IChunk chunk) : BaseFlora(helper, chunk, RandomTulip())
{
    private static IBlock RandomTulip() => Random.Shared.Next(4) switch
    {
        0 => BlocksRegistry.OrangeTulip,
        1 => BlocksRegistry.PinkTulip,
        2 => BlocksRegistry.RedTulip,
        _ => BlocksRegistry.WhiteTulip
    };
}
