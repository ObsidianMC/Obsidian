using Obsidian.API.BlockStates.Builders;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class JungleBushFlora(GenHelper helper, IChunk chunk) : BaseFlora(helper, chunk, Material.JungleLeaves)
{
    private static readonly IBlock jungleLogBlock = BlocksRegistry.Get(Material.JungleLog, new JungleLogStateBuilder().WithAxis(Axis.Y).Build());

    public override async Task GenerateFloraAsync(Vector origin, int seed, int radius, int density)
    {
        for (int rz = 0; rz <= radius * 2; rz++)
        {
            for (int rx = 0; rx <= radius * 2; rx++)
            {
                if (Math.Sqrt((radius - rx) * (radius - rx) + (radius - rz) * (radius - rz)) <= radius)
                {
                    int x = origin.X - radius + rx;
                    int z = origin.Z - radius + rz;
                    int y = await GenHelper.GetWorldHeightAsync(x, z, Chunk) ?? -1;
                    if (y == -1) { continue; }
                    y++;

                    await TryPlaceFloraAsync(new Vector(x, y, z));

                }
            }
        }

        await GenHelper.SetBlockAsync(origin, jungleLogBlock, Chunk);
    }
}
