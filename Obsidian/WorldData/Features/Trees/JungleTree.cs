using Obsidian.API.BlockStates.Builders;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class JungleTree(GenHelper helper, IChunk chunk) : BaseTree(helper, chunk, Material.JungleLeaves, Material.JungleLog, 7)
{
    protected int leavesRadius = 5;

    private static readonly IBlock vineWest = BlocksRegistry.Get(Material.Vine, new VineStateBuilder().IsWest().Build());
    private static readonly IBlock vineSouth = BlocksRegistry.Get(Material.Vine, new VineStateBuilder().IsSouth().Build());
    private static readonly IBlock vineNorth = BlocksRegistry.Get(Material.Vine, new VineStateBuilder().IsNorth().Build());
    private static readonly IBlock vineEast = BlocksRegistry.Get(Material.Vine, new VineStateBuilder().IsEast().Build());
    private static readonly IBlock cocoa = BlocksRegistry.Get(Material.Cocoa, new CocoaStateBuilder().WithAge(2).WithFacing(Facing.South).Build());

    protected override async Task GenerateLeavesAsync(Vector origin, int heightOffset)
    {
        int topY = origin.Y + TrunkHeight + heightOffset + 1;
        List<Vector> vineCandidates = new()
        {
            origin + (0, heightOffset + TrunkHeight - 2, 0)
        };
        for (int y = topY - 2; y < topY + 1; y++)
        {
            for (int x = -leavesRadius; x <= leavesRadius; x++)
            {
                for (int z = -leavesRadius; z <= leavesRadius; z++)
                {
                    if (Math.Sqrt(x * x + z * z) < leavesRadius)
                    {
                        if (await GenHelper.GetBlockAsync(x + origin.X, y, z + origin.Z, Chunk) is { IsAir: true })
                        {
                            await GenHelper.SetBlockAsync(x + origin.X, y, z + origin.Z, LeafBlock, Chunk);
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
                if (await GenHelper.GetBlockAsync(samplePos, Chunk) is IBlock vineBlock && vineBlock.IsAir)
                {
                    var vine = GetVineType(dir);
                    await GenHelper.SetBlockAsync(samplePos, vine, Chunk);

                    // Grow downwards
                    var growAmt = Globals.Random.Next(3, 10);
                    for (int y = -1; y > -growAmt; y--)
                    {
                        if (await GenHelper.GetBlockAsync(samplePos + (0, y, 0), Chunk) is IBlock downward && downward.IsAir)
                        {
                            await GenHelper.SetBlockAsync(samplePos + (0, y, 0), vine, Chunk);
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
            await GenHelper.SetBlockAsync(origin + (0, TrunkHeight + heightOffset - 3, -1), cocoa, Chunk);
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
