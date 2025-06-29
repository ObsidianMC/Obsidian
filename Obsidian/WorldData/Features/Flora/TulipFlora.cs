using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class TulipFlora : BaseFlora
{
    public TulipFlora(GenHelper helper, IChunk chunk) : base(helper, chunk)
    {
        int tulipType = Random.Shared.Next(3);
        this.FloraMat = tulipType switch
        {
            0 => Material.OrangeTulip,
            1 => Material.PinkTulip,
            2 => Material.RedTulip,
            _ => Material.WhiteTulip
        };
    }
}
