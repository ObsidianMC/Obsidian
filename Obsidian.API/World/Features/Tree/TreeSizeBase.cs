using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree;
public abstract class TreeSizeBase
{
    [Range(0, 80)]
    public float MinClippedHeight { get; set; }

    public abstract string Type { get; init; }
}
