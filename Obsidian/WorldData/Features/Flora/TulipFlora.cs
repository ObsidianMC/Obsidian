using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class TulipFlora : BaseFlora
{
    public TulipFlora(GenHelper helper, IChunk chunk) : base(helper, chunk)
    {
        int tulipType = Random.Shared.Next(3);
        this.FloraKind = tulipType switch
        {
            0 => BlocksRegistry.OrangeTulip,
            1 => BlocksRegistry.PinkTulip,
            2 => BlocksRegistry.RedTulip,
            _ => BlocksRegistry.WhiteTulip
        };
    }
}
