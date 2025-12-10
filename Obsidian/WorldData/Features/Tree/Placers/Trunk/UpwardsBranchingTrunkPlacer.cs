using Obsidian.API.Utilities;
using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using Obsidian.Registries;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:upwards_branching_trunk_placer")]
public sealed class UpwardsBranchingTrunkPlacer : TrunkPlacer
{
    public override required string Type { get; init; }

    public required IIntProvider ExtraBranchSteps { get; init; }

    public required IIntProvider ExtraBranchLength { get; init; }

    [Range(0.0, 1.0)]
    public required float PlaceBranchPerLogProbability { get; init; }

    public string CanGrowThrough { get; init; }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, int treeHeight, IBlock trunkBlock)
    {
        var random = context.Random;
        var trunkPositions = new List<Vector>();
        var logPos = origin;

        // Place main trunk with probabilistic branches
        for (int heightPos = 0; heightPos < treeHeight; heightPos++)
        {
            int currentHeight = origin.Y + heightPos;
            logPos = new Vector(origin.X, currentHeight, origin.Z);

            // Place main trunk log
            if (await PlaceLog(context, logPos, trunkBlock)
                && heightPos < treeHeight - 1
                && random.NextDouble() < PlaceBranchPerLogProbability)
            {
                // Randomly place a branch
                var cardinalDirs = Vector.CardinalDirs.ToArray();
                var branchDir = cardinalDirs[random.Next(cardinalDirs.Length)];
                int branchLen = ExtraBranchLength.Get();
                int branchPos = Math.Max(0, branchLen - ExtraBranchLength.Get() - 1);
                int branchSteps = ExtraBranchSteps.Get();

                await PlaceBranch(
                    context,
                    treeHeight,
                    trunkBlock,
                    trunkPositions,
                    currentHeight,
                    branchDir,
                    branchPos,
                    branchSteps
                );
            }

            // Add top foliage attachment
            if (heightPos == treeHeight - 1)
            {
                trunkPositions.Add(new Vector(origin.X, currentHeight + 1, origin.Z));
            }
        }

        return trunkPositions;
    }

    private async ValueTask PlaceBranch(
        FeatureContext context,
        int treeHeight,
        IBlock trunkBlock,
        List<Vector> attachments,
        int currentHeight,
        Vector branchDir,
        int branchPos,
        int branchSteps
    )
    {
        int heightAlongBranch = currentHeight + branchPos;
        int logX = context.PlacementLocation.X;
        int logZ = context.PlacementLocation.Z;
        int branchPlacementIndex = branchPos;

        while (branchPlacementIndex < treeHeight && branchSteps > 0)
        {
            if (branchPlacementIndex >= 1)
            {
                int placementHeight = currentHeight + branchPlacementIndex;
                logX += branchDir.X;
                logZ += branchDir.Z;
                heightAlongBranch = placementHeight;

                var branchLogPos = new Vector(logX, placementHeight, logZ);
                if (await PlaceLog(context, branchLogPos, trunkBlock))
                {
                    heightAlongBranch = placementHeight + 1;
                }

                attachments.Add(branchLogPos);
            }

            branchPlacementIndex++;
            branchSteps--;
        }

        // Add additional foliage attachments if branch extended upward significantly
        if (heightAlongBranch - currentHeight > 1)
        {
            var foliagePos = new Vector(logX, heightAlongBranch, logZ);
            attachments.Add(foliagePos);
            attachments.Add(foliagePos + new Vector(0, -2, 0));
        }
    }

    private async ValueTask<bool> PlaceLog(FeatureContext context, Vector pos, IBlock trunkBlock)
    {
        var existingBlock = await context.World.GetBlockAsync(pos);

        // Check if we can place here (replaceable or can grow through)
        if (existingBlock != null)
        {
            bool canReplace = TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId);
            bool canGrowThrough = CanGrowThrough.Contains(existingBlock.UnlocalizedName);

            if (canReplace || canGrowThrough)
            {
                await context.World.SetBlockUntrackedAsync(pos, trunkBlock, false);
                return true;
            }
        }

        return false;
    }
}
