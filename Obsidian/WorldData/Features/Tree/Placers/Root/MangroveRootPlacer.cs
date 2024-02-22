using Obsidian.API.World.Features.Tree;
using System.ComponentModel.DataAnnotations;

namespace Obsidian.WorldData.Features.Tree.Placers.Root;

[TreeProperty("minecraft:mangrove_root_placer")]
public sealed class MangroveRootPlacer : RootPlacer
{
    public override required string Type { get; init; } = "minecraft:mangrove_root_placer";

    public required MangrovePlacement MangroveRootPlacement { get; set; }

    public sealed class MangrovePlacement
    {
        [Range(1, 12)]
        public required int MaxRootWidth { get; set; }

        [Range(1, 12)]
        public required int MaxRootLength { get; set; }

        [Range(0.0, 1.0)]
        public required float RandomSkewChance { get; set; }

        public List<string> CanGrowThrough { get; set; } = [];
        public List<string> MuddyRootsIn { get; set; } = [];

        public required IBlockStateProvider MuddyRootsProvider { get; set; }
    }
}
