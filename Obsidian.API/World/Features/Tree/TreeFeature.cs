using System.Text.Json.Serialization;

namespace Obsidian.API.World.Features.Tree;

public sealed class TreeFeature : ConfiguredFeatureBase
{
    [JsonIgnore]
    public override string Type => "minecraft:tree";

    public override required string Identifier { get; init; }

    public bool IgnoreVines { get; set; }
    public bool ForceDirt { get; set; }

    public required TreeSizeBase MinimumSize { get; init; }
    public required TrunkPlacer TrunkPlacer { get; init; }
    public required FoliagePlacer FoliagePlacer { get; init; }

    public required IBlockStateProvider DirtProvider { get; init; }
    public required IBlockStateProvider TrunkProvider { get; init; }
    public required IBlockStateProvider FoliageProvider { get; init; }

    public RootPlacer? RootPlacer { get; init; }

    public List<DecoratorBase> Decorators { get; set; } = [];

    public override async ValueTask Place(FeatureContext context)
    {
        var world = context.World;
        var random = context.Random;
        var origin = context.PlacementLocation;

        // Calculate tree dimensions
        var treeHeight = this.TrunkPlacer.GetTreeHeight(random);
        var foliageHeight = this.FoliagePlacer.GetFoliageHeight(random, treeHeight);
        var rootHeight = treeHeight - foliageHeight;
        var foliageRadius = this.FoliagePlacer.FoliageRadius(random, rootHeight);

        // Get trunk origin (may be offset by root placer)
        var trunkOrigin = this.RootPlacer?.GetTrunkOrigin(origin) ?? origin;

        // Validate tree can be placed
        if (!await CanPlace(world, origin, trunkOrigin, treeHeight))
            return;

        // Place dirt at the base if configured
        if (this.ForceDirt)
        {
            var dirtBlock = this.DirtProvider.Get();
            var belowOrigin = origin + Vector.Down;
            await world.SetBlockAsync(belowOrigin, dirtBlock);
        }

        // Place roots if root placer exists and collect positions
        var rootPositions = new List<Vector>();
        if (this.RootPlacer != null)
        {
            rootPositions = await this.RootPlacer.Place(context, origin, trunkOrigin);
        }

        // Place trunk and collect positions
        var trunkBlock = this.TrunkProvider.Get();
        var trunkPositions = await this.TrunkPlacer.Place(context, trunkOrigin, treeHeight, trunkBlock);

        // Place foliage and collect positions
        var foliageBlock = this.FoliageProvider.Get();
        var foliagePositions = await this.FoliagePlacer.Place(context, trunkPositions, treeHeight, foliageBlock);

        // Apply decorators (beehives, vines, ground alterations, etc.)
        foreach (var decorator in this.Decorators)
        {
            await decorator.Place(context, trunkPositions, foliagePositions, rootPositions);
        }
    }

    /// <summary>
    /// Validates that the tree can be placed at the specified location.
    /// Checks for sufficient space and valid ground blocks.
    /// </summary>
    private static async ValueTask<bool> CanPlace(IWorld world, Vector origin, Vector trunkOrigin, int treeHeight)
    {
        // Check if ground block below origin is suitable (dirt, grass, etc.)
        var groundPos = origin + Vector.Down;
        var groundBlock = await world.GetBlockAsync(groundPos);

        if (!IsValidGroundBlock(groundBlock))
            return false;

        // Check if there's enough vertical space (world height limit is typically 320)
        var topPos = trunkOrigin + (0, treeHeight, 0);
        if (topPos.Y >= 320) // Maximum build height
            return false;

        return true;
    }

    /// <summary>
    /// Checks if a block is valid ground for a tree (dirt, grass, podzol, etc.).
    /// </summary>
    private static bool IsValidGroundBlock(IBlock block)
    {
        // Trees can grow on dirt, grass, podzol, mycelium, etc.
        return block.Material == Material.Dirt ||
               block.Material == Material.GrassBlock ||
               block.Material == Material.Podzol ||
               block.Material == Material.Mycelium ||
               block.Material == Material.RootedDirt;
    }
}
