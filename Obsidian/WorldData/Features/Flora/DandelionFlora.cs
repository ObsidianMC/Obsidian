using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class DandelionFlora : BaseFlora
{
    public DandelionFlora(GenHelper helper, Chunk chunk) : base(helper, chunk, Material.Dandelion)
    {

    }

}
