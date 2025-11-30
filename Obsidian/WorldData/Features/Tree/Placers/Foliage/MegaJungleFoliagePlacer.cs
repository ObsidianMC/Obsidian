using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for large jungle trees (2x2 trunk) - dense tropical canopy with variable height.
/// </summary>
[TreeProperty("minecraft:mega_jungle_foliage_placer")]
public sealed class MegaJungleFoliagePlacer : FoliagePlacer
{
	public override required string Type { get; init; }

	/// <summary>
	/// Height of the jungle foliage (0-16 blocks).
	/// </summary>
	[Range(0, 16)]
	public required int Height { get; init; }

	public override int GetFoliageHeight(Random random, int treeHeight)
	{
		return Height;
	}

	public override async ValueTask<List<Vector>> Place(FeatureContext context, List<Vector> trunkPositions, int treeHeight, IBlock foliageBlock)
	{
		var random = context.Random;
		var placedPositions = new List<Vector>();

		foreach (var attachment in trunkPositions)
		{
			int leafRadius = FoliageRadius(random, treeHeight);
			int offset = GetOffset(random);
			int foliageHeight = GetFoliageHeight(random, treeHeight);
			bool doubleTrunk = trunkPositions.Count > 1; // Mega jungle trees have 2x2 trunks
			int radiusOffset = 0;

			var foliagePos = attachment;

			// Mega jungle: leaf height varies based on trunk type
			// For double trunk: use full foliageHeight, for single: 1 + random(2)
			int leafHeight = doubleTrunk ? foliageHeight : 1 + random.Next(2);

			// Place leaves from offset down to offset - leafHeight
			for (int yo = offset; yo >= offset - leafHeight; yo--)
			{
				// Mega jungle: radius = leafRadius + radiusOffset + 1 - yo
				int currentRadius = leafRadius + radiusOffset + 1 - yo;
				await FoliagePlacerHelper.PlaceLeavesRow(
					context.World,
					random,
					foliageBlock,
					foliagePos,
					currentRadius,
					yo,
					doubleTrunk,
					ShouldSkipLocation,
					placedPositions
				);
			}
		}

		return placedPositions;
	}

	protected override bool ShouldSkipLocation(Random random, int dx, int y, int dz, int currentRadius, bool doubleTrunk)
	{
		// Mega jungle: skip if dx + dz >= 7 OR if outside circular radius
		// This creates a diamond-like cutoff at corners plus circular edges
		if (dx + dz >= 7)
			return true;

		return dx * dx + dz * dz > currentRadius * currentRadius;
	}
}
