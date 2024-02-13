using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree.Placers.Root;
public sealed class MangroveRootPlacer : RootPlacer
{
    public override string Type => "mangrove_root_placer";

    public required MangrovePlacement MangroveRootPlacement { get; set; }

    public sealed class MangrovePlacement
    {
        [Range(1, 12)]
        public required int MaxRootWidth { get; set; }

        [Range(1, 12)]
        public required int MaxRootLength { get; set; }

        [Range(0.0, 1.0)]
        public required float RandomSkewChance { get; set; }

        public List<string> CanGrowThrough { get; } = [];
        public List<string> MuddyRootsIn { get; } = [];

        public required IBlockStateProvider MuddyRootsProvider { get; set; }
    }
}
