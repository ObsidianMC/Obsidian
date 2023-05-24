using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class PoppyFlora : BaseFlora
{
    public PoppyFlora(GenHelper helper, Chunk chunk) : base(helper, chunk, Material.Poppy)
    {

    }

}
