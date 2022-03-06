using System;

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees;

public class LargeJungleTree : JungleTree
{
    private readonly Random rand = new();

    public LargeJungleTree(World world) : base(world)
    {
        leavesRadius = 6;
        trunkHeight = 20;
    }

    protected override async Task GenerateLeavesAsync(Vector origin, int heightOffset)
    {
        List<Vector> vineCandidates = new();
        int topY = origin.Y + trunkHeight + heightOffset + 1;

        var leafBlock = new Block(leaf);
        for (int y = topY - 3; y < topY + 1; y++)
        {
            for (int x = -leavesRadius; x <= leavesRadius + 1; x++)
            {
                for (int z = -leavesRadius; z <= leavesRadius + 1; z++)
                {
                    if (Math.Sqrt((x - 0.5) * (x - 0.5) + (z - 0.5) * (z - 0.5)) < leavesRadius)
                    {
                        if (await world.GetBlockAsync(x + origin.X, y, z + origin.Z) is { IsAir: true })
                        {
                            await world.SetBlockUntrackedAsync(x + origin.X, y, z + origin.Z, leafBlock);
                            if (rand.Next(4) == 0)
                            {
                                vineCandidates.Add(new Vector(x + origin.X, y, z + origin.Z));
                            }
                        }
                    }
                }
            }
            leavesRadius--;
        }
        await PlaceVinesAsync(vineCandidates);
    }

    protected override async Task GenerateTrunkAsync(Vector origin, int heightOffset)
    {
        List<Vector> vineCandidates = new();
        int topY = trunkHeight + heightOffset;

        for (int x = 0; x < 2; x++)
        {
            for (int z = 0; z < 2; z++)
            {
                for (int y = topY; y > 0; y--)
                {
                    await world.SetBlockUntrackedAsync(origin + (x, y, z), new Block(trunk, 1));

                    // Roll the dice to place a vine on this trunk block.
                    if (rand.Next(10) == 0)
                    {
                        vineCandidates.Add(origin + (x, y, z));
                    }
                }

                // Fill in any air gaps under the trunk
                var under = await world.GetBlockAsync(origin + (x, -1, z));
                if (under.Value.Material != Material.GrassBlock)
                {
                    await world.SetBlockUntrackedAsync(origin + (x, -1, z), new Block(trunk, 1));
                }
            }
        }
        await PlaceVinesAsync(vineCandidates);
    }
}
