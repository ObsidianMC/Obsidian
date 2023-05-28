using Obsidian.WorldData.Generators;


namespace Obsidian.WorldData.Features.Flora;

public class FernFlora : BaseFlora
{
    public FernFlora(GenHelper helper, Chunk chunk) : base(helper, chunk, Material.Fern)
    {

    }
}
