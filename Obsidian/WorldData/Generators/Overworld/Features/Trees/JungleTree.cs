using System;

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees;

public class JungleTree : BaseTree
{
    protected int leavesRadius = 5;

    private readonly Block vineWest = new Block(4891);
    private readonly Block vineSouth = new Block(4888);
    private readonly Block vineNorth = new Block(4884);
    private readonly Block vineEast = new Block(4876);
    private readonly Random rand = new();

    public JungleTree(World world) : base(world, Material.JungleLeaves, Material.JungleLog, 6)
    {
    }

    protected override async Task GenerateLeavesAsync(Vector origin, int heightOffset)
    {
        int topY = origin.Y + trunkHeight + heightOffset + 1;
        List<Vector> vineCandidates = new()
        {
            origin + (0, heightOffset + trunkHeight - 2, 0)
        };

        var leafBlock = new Block(leaf);
        int radius = leavesRadius;
        for (int y = topY - 2; y < topY + 1; y++)
        {
            for (int x = -leavesRadius; x <= leavesRadius; x++)
            {
                for (int z = -leavesRadius; z <= leavesRadius; z++)
                {
                    if (inCircle(x, z, radius))
                    {
                        await world.SetBlockUntrackedAsync(x + origin.X, y, z + origin.Z, leafBlock);
                        if (rand.Next(5) % 2 == 0)
                        {
                            vineCandidates.Add(new Vector(x + origin.X, y, z + origin.Z));
                        }
                    }
                }
            }
            radius--;
        }
        await PlaceVinesAsync(vineCandidates);
    }

    protected async Task PlaceVinesAsync(List<Vector> candidates)
    {
        foreach (var candidate in candidates)
        {
            // Check sides for air
            foreach (var dir in Vector.CardinalDirs)
            {
                var samplePos = candidate + dir;
                if (await world.GetBlockAsync(samplePos) is Block vineBlock && vineBlock.IsAir)
                {
                    var vine = GetVineType(dir);
                    await world.SetBlockAsync(samplePos, vine);

                    // Grow downwards
                    var growAmt = rand.Next(7);
                    for (int y = -1; y > -growAmt; y--)
                    {
                        if (await world.GetBlockAsync(samplePos + (0, y, 0)) is Block downward && downward.IsAir)
                        {
                            await world.SetBlockAsync(samplePos + (0, y, 0), vine);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
        }
    }

    private bool inCircle(int x, int z, int r)
    {
        return Math.Sqrt(x * x + z * z) < r;
    }

    private Block GetVineType(Vector vec) => vec switch
    {
        { X: 1, Z: 0 } => vineWest,
        { X: -1, Z: 0 } => vineEast,
        { X: 0, Z: 1 } => vineNorth,
        { X: 0, Z: -1 } => vineSouth,
        _ => new Block(0)
    };
}
