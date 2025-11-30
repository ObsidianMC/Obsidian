using Obsidian.API.Utilities;
using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using Obsidian.Registries;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Trunk;

[TreeProperty("minecraft:cherry_trunk_placer")]
public sealed class CherryTrunkPlacer : TrunkPlacer
{
    public override required string Type { get; init; }

    [Range(1, 3)]
    public required IIntProvider BranchCount { get; init; }

    [Range(2, 16)]
    public required IIntProvider BranchHorizontalLength { get; init; }

    [Range(-16, 0)]
    public required IntProviderRangeValue BranchStartOffsetFromTop { get; init; }

    [Range(-16, 16)]
    public required IIntProvider BranchEndOffsetFromTop { get; init; }

    public override async ValueTask<List<Vector>> Place(FeatureContext context, Vector origin, int treeHeight, IBlock trunkBlock)
    {
        var random = context.Random;
        var trunkPositions = new List<Vector>();

        // Set dirt below origin
        await context.World.SetBlockUntrackedAsync(origin + Vector.Down, BlocksRegistry.Dirt, false);

        // Calculate branch offsets from origin
        int firstBranchOffsetFromOrigin = Math.Max(0, treeHeight - 1 + random.Next(BranchStartOffsetFromTop.MaxInclusive - BranchStartOffsetFromTop.MinInclusive + 1) + BranchStartOffsetFromTop.MinInclusive);

        // Second branch uses same min but max-1 to ensure variation
        int secondBranchMin = BranchStartOffsetFromTop.MinInclusive;
        int secondBranchMax = BranchStartOffsetFromTop.MaxInclusive - 1;
        int secondBranchOffsetFromOrigin = Math.Max(0, treeHeight - 1 + random.Next(secondBranchMax - secondBranchMin + 1) + secondBranchMin);

        if (secondBranchOffsetFromOrigin >= firstBranchOffsetFromOrigin)
        {
            secondBranchOffsetFromOrigin++;
        }

        int branchCount = BranchCount.Get();
        bool hasMiddleBranch = branchCount == 3;
        bool hasBothSideBranches = branchCount >= 2;

        int trunkHeight;
        if (hasMiddleBranch)
        {
            trunkHeight = treeHeight;
        }
        else if (hasBothSideBranches)
        {
            trunkHeight = Math.Max(firstBranchOffsetFromOrigin, secondBranchOffsetFromOrigin) + 1;
        }
        else
        {
            trunkHeight = firstBranchOffsetFromOrigin + 1;
        }

        // Place main trunk
        for (int y = 0; y < trunkHeight; y++)
        {
            var pos = origin + new Vector(0, y, 0);
            var existingBlock = await context.World.GetBlockAsync(pos);
            if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
            {
                await context.World.SetBlockUntrackedAsync(pos, trunkBlock, false);
            }
        }

        // Add middle branch attachment if present
        if (hasMiddleBranch)
        {
            trunkPositions.Add(origin + new Vector(0, trunkHeight, 0));
        }

        // Generate first branch
        var cardinalDirs = Vector.CardinalDirs.ToArray();
        var branchDirection = cardinalDirs[random.Next(cardinalDirs.Length)];

        var firstBranchAttachment = await GenerateBranch(
            context,
            random,
            treeHeight,
            origin,
            trunkBlock,
            branchDirection,
            firstBranchOffsetFromOrigin,
            firstBranchOffsetFromOrigin < trunkHeight - 1
        );
        trunkPositions.Add(firstBranchAttachment);

        // Generate second branch if needed
        if (hasBothSideBranches)
        {
            var secondBranchAttachment = await GenerateBranch(
                context,
                random,
                treeHeight,
                origin,
                trunkBlock,
                -branchDirection, // Opposite direction
                secondBranchOffsetFromOrigin,
                secondBranchOffsetFromOrigin < trunkHeight - 1
            );
            trunkPositions.Add(secondBranchAttachment);
        }

        return trunkPositions;
    }

    private async ValueTask<Vector> GenerateBranch(
        FeatureContext context,
        Random random,
        int treeHeight,
        Vector origin,
        IBlock trunkBlock,
        Vector branchDirection,
        int offsetFromOrigin,
        bool middleContinuesUpwards
    )
    {
        var logPos = origin + new Vector(0, offsetFromOrigin, 0);
        int branchEndPosOffsetFromOrigin = treeHeight - 1 + BranchEndOffsetFromTop.Get();
        bool extendBranchAwayFromTrunk = middleContinuesUpwards || branchEndPosOffsetFromOrigin < offsetFromOrigin;
        int distanceToTrunk = BranchHorizontalLength.Get() + (extendBranchAwayFromTrunk ? 1 : 0);

        var branchEndPos = origin + (branchDirection * distanceToTrunk) + new Vector(0, branchEndPosOffsetFromOrigin, 0);
        int stepsHorizontally = extendBranchAwayFromTrunk ? 2 : 1;

        // Place initial horizontal logs
        for (int i = 0; i < stepsHorizontally; i++)
        {
            logPos += branchDirection;
            var existingBlock = await context.World.GetBlockAsync(logPos);
            if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
            {
                // Note: In Minecraft, these logs would have axis set based on direction
                // For now, we place without axis rotation
                await context.World.SetBlockUntrackedAsync(logPos, trunkBlock, false);
            }
        }

        // Grow branch toward end position using Manhattan distance probability
        var verticalDirection = branchEndPos.Y > logPos.Y ? Vector.Up : Vector.Down;

        while (true)
        {
            int distance = Math.Abs(branchEndPos.X - logPos.X) + Math.Abs(branchEndPos.Y - logPos.Y) + Math.Abs(branchEndPos.Z - logPos.Z);
            if (distance == 0)
            {
                return branchEndPos + Vector.Up;
            }

            float chanceToGrowVertically = (float)Math.Abs(branchEndPos.Y - logPos.Y) / distance;
            bool growVertically = random.NextDouble() < chanceToGrowVertically;

            logPos += growVertically ? verticalDirection : branchDirection;

            var existingBlock = await context.World.GetBlockAsync(logPos);
            if (existingBlock != null && TagsRegistry.Block.Replaceable.Entries.Contains(existingBlock.RegistryId))
            {
                await context.World.SetBlockUntrackedAsync(logPos, trunkBlock, false);
            }
        }
    }
}
