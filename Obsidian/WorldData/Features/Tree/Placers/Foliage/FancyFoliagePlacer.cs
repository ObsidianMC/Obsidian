using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Foliage;

/// <summary>
/// Foliage placer for fancy (large) oak trees - irregular rounded canopy with circular skip logic.
/// </summary>
[TreeProperty("minecraft:fancy_foliage_placer")]
public sealed class FancyFoliagePlacer : FoliagePlacer
{
	public override required string Type { get; init; }

	/// <summary>
	/// Height of the fancy oak foliage (0-16 blocks).
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
			bool doubleTrunk = false; // Fancy oaks are single trunk
			int radiusOffset = 0;

			var foliagePos = attachment;

			// Fancy oak places leaves from offset down to offset - foliageHeight
			for (int yo = offset; yo >= offset - foliageHeight; yo--)
			{
				// Fancy: radius is larger on middle layers (adds 1 if not top or bottom)
				int currentRadius = leafRadius + (yo != offset && yo != offset - foliageHeight ? 1 : 0);
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
		// Fancy: circular skip - (dx + 0.5)² + (dz + 0.5)² > radius²
		// This creates smooth circular edges
		float dxOffset = dx + 0.5f;
		float dzOffset = dz + 0.5f;
		return (dxOffset * dxOffset + dzOffset * dzOffset) > currentRadius * currentRadius;
	}
}
