namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public class TulipFlora : BaseFlora
{
    public TulipFlora(World world) : base(world)
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
