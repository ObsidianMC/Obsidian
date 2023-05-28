using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class TulipFlora : BaseFlora
{
    public TulipFlora(GenHelper helper, Chunk chunk) : base(helper, chunk)
    {
        var seedRand = new Random();
        int tulipType = seedRand.Next(3);
        this.FloraMat = tulipType switch
        {
            0 => Material.OrangeTulip,
            1 => Material.PinkTulip,
            2 => Material.RedTulip,
            _ => Material.WhiteTulip
        };
    }
}
