using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees;

public class JungleTree : BaseTree
{
    protected int leavesRadius = 5;

    private readonly IBlock vineWest = BlocksRegistry.Get(4891);
    private readonly IBlock vineSouth = BlocksRegistry.Get(4888);
    private readonly IBlock vineNorth = BlocksRegistry.Get(4884);
    private readonly IBlock vineEast = BlocksRegistry.Get(4876);

    public JungleTree(GenHelper helper, Chunk chunk) : base(helper, chunk, Material.JungleLeaves, Material.JungleLog, 7)
    {
    }

    protected override async Task GenerateLeavesAsync(Vector origin, int heightOffset)
    {
        int topY = origin.Y + trunkHeight + heightOffset + 1;
        List<Vector> vineCandidates = new()
        {
            origin + (0, heightOffset + trunkHeight - 2, 0)
        };
        for (int y = topY - 2; y < topY + 1; y++)
        {
            for (int x = -leavesRadius; x <= leavesRadius; x++)
            {
                for (int z = -leavesRadius; z <= leavesRadius; z++)
                {
                    if (Math.Sqrt(x * x + z * z) < leavesRadius)
                    {
                        if (await helper.GetBlockAsync(x + origin.X, y, z + origin.Z, chunk) is { IsAir: true })
                        {
                            await helper.SetBlockAsync(x + origin.X, y, z + origin.Z, leafBlock, chunk);
                            if (Globals.Random.Next(3) == 0)
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

    protected async Task PlaceVinesAsync(List<Vector> candidates)
    {
        foreach (var candidate in candidates)
        {
            // Check sides for air
            foreach (var dir in Vector.CardinalDirs)
            {
                var samplePos = candidate + dir;
                if (await helper.GetBlockAsync(samplePos, chunk) is IBlock vineBlock && vineBlock.IsAir)
                {
                    var vine = GetVineType(dir);
                    await helper.SetBlockAsync(samplePos, vine, chunk);

                    // Grow downwards
                    var growAmt = Globals.Random.Next(3, 10);
                    for (int y = -1; y > -growAmt; y--)
                    {
                        if (await helper.GetBlockAsync(samplePos + (0, y, 0), chunk) is IBlock downward && downward.IsAir)
                        {
                            await helper.SetBlockAsync(samplePos + (0, y, 0), vine, chunk);
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

    protected override async Task GenerateTrunkAsync(Vector origin, int heightOffset)
    {
        await base.GenerateTrunkAsync(origin, heightOffset);
        if (Globals.Random.Next(3) == 0)
        {
            await helper.SetBlockAsync(origin + (0, trunkHeight + heightOffset - 3, -1), BlocksRegistry.Cocoa, chunk);//TODO state == 9
        }
    }

    protected IBlock GetVineType(Vector vec) => vec switch
    {
        { X: 1, Z: 0 } => vineWest,
        { X: -1, Z: 0 } => vineEast,
        { X: 0, Z: 1 } => vineNorth,
        { X: 0, Z: -1 } => vineSouth,
        _ => BlocksRegistry.Air
    };
}
