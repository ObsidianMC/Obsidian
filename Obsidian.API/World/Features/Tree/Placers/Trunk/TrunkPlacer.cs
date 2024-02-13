using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree.Placers;
public abstract class TrunkPlacer
{
    public abstract string Type { get; }

    [Range(0, 32)]
    public virtual int BaseHeight { get; set; }

    [Range(0, 24)]
    public virtual int HeightRandA { get; set; }

    [Range(0, 24)]
    public virtual int HeightRandB { get; init; }
}
