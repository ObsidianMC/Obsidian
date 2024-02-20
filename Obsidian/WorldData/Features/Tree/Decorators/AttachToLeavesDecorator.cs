using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Decorators;


[TreeProperty("minecraft:attached_to_leaves")]
public sealed class AttachToLeavesDecorator : DecoratorBase
{
    public override string Type { get; init; } = "minecraft:attached_to_leaves";

    [Range(0, 16)]
    public required int ExclusionRadiusXZ { get; set; }

    [Range(0, 16)]
    public required int ExclusionRadiusY { get; set; }

    [Range(0, 16)]
    public required int RequiredEmptyBlocks { get; set; }

    public required IBlockStateProvider BlockProvider { get; set; }

    /// <summary>
    /// Directions to generate. 
    /// </summary>
    /// <remarks>
    /// Cannot be empty. 
    /// Must be up, down, north, south , west, or east.
    /// </remarks>
    public List<string> Directions { get; } = [];
}
