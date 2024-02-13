namespace Obsidian.API.World.Features.Tree.Placers.Foliage;
public sealed class SpruceFoliagePlacer : FoliagePlacer
{
    public override string Type => "spruce_foliage_placer";

    public required IIntProvider TrunkHeight { get; set; }
}
