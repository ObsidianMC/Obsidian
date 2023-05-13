using Obsidian.API.BlockStates.Builders;

namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public class BambooFlora : BaseTallFlora
{
    public BambooFlora(GenHelper helper, Chunk chunk) : 
        base(helper, chunk, Material.Bamboo, 15)
    {

    }
}
