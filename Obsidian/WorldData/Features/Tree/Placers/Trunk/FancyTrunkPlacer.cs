using Obsidian.API.Utilities;
using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using Obsidian.Registries;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:fancy_trunk_placer")]
public sealed class FancyTrunkPlacer : TrunkPlacer
{
    public override required string Type { get; init; }

    private const double TrunkHeightScale = 0.618;
    private const double ClusterDensityMagic = 1.382;
    private const double BranchSlope = 0.381;
    private const double BranchLengthMagic = 0.328;

    public override async ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, int treeHeight, IBlock trunkBlock)
    {
        var random = context.Random;
        int height = treeHeight + 2;
        int trunkHeight = (int)Math.Floor(height * TrunkHeightScale);

        // Set dirt below origin
        await context.World.SetBlockUntrackedAsync(origin + Vector.Down, BlocksRegistry.Dirt, false);

        double foliageDensity = 1.0;
        int clustersPerY = Math.Max(1, (int)Math.Floor(ClusterDensityMagic + Math.Pow(foliageDensity * height / 13.0, 2.0)));
        int trunkTop = origin.Y + trunkHeight;
        int relativeY = height - 5;

        var foliageCoords = new List<FoliageCoords>();
        foliageCoords.Add(new FoliageCoords(origin + new Vector(0, relativeY, 0), trunkTop));

        // Generate branch positions
        for (; relativeY >= 0; relativeY--)
        {
            float treeShape = CalculateTreeShape(height, relativeY);
            if (!(treeShape < 0.0f))
            {
                for (int i = 0; i < clustersPerY; i++)
                {
                    double widthScale = 1.0;
                    double radius = widthScale * treeShape * (random.NextDouble() + BranchLengthMagic);
                    double angle = random.NextDouble() * 2.0 * Math.PI;
                    double x = radius * Math.Sin(angle) + 0.5;
                    double z = radius * Math.Cos(angle) + 0.5;

                    var checkStart = origin + new Vector((int)Math.Floor(x), relativeY - 1, (int)Math.Floor(z));
                    var checkEnd = checkStart + new Vector(0, 5, 0);

                    if (await MakeLimb(context, checkStart, checkEnd, false, trunkBlock))
                    {
                        int dx = origin.X - checkStart.X;
                        int dz = origin.Z - checkStart.Z;
                        double branchHeight = checkStart.Y - Math.Sqrt(dx * dx + dz * dz) * BranchSlope;
                        int branchTop = branchHeight > trunkTop ? trunkTop : (int)branchHeight;
                        var checkBranchBase = new Vector(origin.X, branchTop, origin.Z);

                        if (await MakeLimb(context, checkBranchBase, checkStart, false, trunkBlock))
                        {
                            foliageCoords.Add(new FoliageCoords(checkStart, checkBranchBase.Y));
                        }
                    }
                }
            }
        }

        // Place main trunk
        await MakeLimb(context, origin, origin + new Vector(0, trunkHeight, 0), true, trunkBlock);

        // Place all branches
        await MakeBranches(context, height, origin, foliageCoords, trunkBlock);

        // Build attachment list (trim low branches)
        var trunkPositions = new List<Vector>();
        foreach (var foliageCoord in foliageCoords)
        {
            if (TrimBranches(height, foliageCoord.BranchBase - origin.Y))
            {
                trunkPositions.Add(foliageCoord.Position);
            }
        }

        return trunkPositions;
    }

    private async ValueTask<bool> MakeLimb(
        FeatureContext context,
        Vector startPos,
        Vector endPos,
        bool doPlace,
        IBlock trunkBlock)
    {
        if (!doPlace && startPos == endPos)
        {
            return true;
        }

        var delta = endPos - startPos;
        int steps = GetSteps(delta);
        float dx = (float)delta.X / steps;
        float dy = (float)delta.Y / steps;
        float dz = (float)delta.Z / steps;

        for (int i = 0; i <= steps; i++)
        {
            var blockPos = startPos + new Vector(
                (int)Math.Floor(0.5f + i * dx),
                (int)Math.Floor(0.5f + i * dy),
                (int)Math.Floor(0.5f + i * dz)
            );

            if (doPlace)
            {
                var existingBlock = await context.World.GetBlockAsync(blockPos);
                if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
                {
                    // Note: In Minecraft, fancy trunk logs have proper axis orientation based on direction
                    // For now, we place as-is without axis rotation
                    await context.World.SetBlockUntrackedAsync(blockPos, trunkBlock, false);
                }
            }
            else
            {
                var existingBlock = await context.World.GetBlockAsync(blockPos);
                if (existingBlock == null || !TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private int GetSteps(Vector pos)
    {
        int absX = Math.Abs(pos.X);
        int absY = Math.Abs(pos.Y);
        int absZ = Math.Abs(pos.Z);
        return Math.Max(absX, Math.Max(absY, absZ));
    }

    private bool TrimBranches(int height, int localY)
    {
        return localY >= height * 0.2;
    }

    private async ValueTask MakeBranches(
        FeatureContext context,
        int height,
        Vector origin,
        List<FoliageCoords> foliageCoords,
        IBlock trunkBlock)
    {
        foreach (var endCoord in foliageCoords)
        {
            int branchBase = endCoord.BranchBase;
            var baseCoord = new Vector(origin.X, branchBase, origin.Z);

            if (baseCoord != endCoord.Position && TrimBranches(height, branchBase - origin.Y))
            {
                await MakeLimb(context, baseCoord, endCoord.Position, true, trunkBlock);
            }
        }
    }

    private static float CalculateTreeShape(int height, int y)
    {
        if (y < height * 0.3f)
        {
            return -1.0f;
        }
        else
        {
            float radius = height / 2.0f;
            float adjacent = radius - y;
            float distance = (float)Math.Sqrt(radius * radius - adjacent * adjacent);

            if (adjacent == 0.0f)
            {
                distance = radius;
            }
            else if (Math.Abs(adjacent) >= radius)
            {
                return 0.0f;
            }

            return distance * 0.5f;
        }
    }

    private class FoliageCoords
    {
        public Vector Position { get; }
        public int BranchBase { get; }

        public FoliageCoords(Vector pos, int branchBase)
        {
            Position = pos;
            BranchBase = branchBase;
        }
    }
}
