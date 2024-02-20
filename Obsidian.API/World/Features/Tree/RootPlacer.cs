using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree;
public abstract class RootPlacer
{
    public abstract string Type { get; init; }

    public virtual required IBlockStateProvider RootProvider { get; set; }
    public virtual required IIntProvider TrunkOffsetY { get; set; }

    public virtual RootPlacement? AboveRootPlacement { get; }

    public sealed class RootPlacement
    {
        public required IBlockStateProvider AboveRootProvider { get; init; }

        [Range(0.0, 1.0)]
        public required float PlacementChance { get; init; }
    }
}
