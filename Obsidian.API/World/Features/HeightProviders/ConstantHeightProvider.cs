namespace Obsidian.API.World.Features.HeightProviders;
public sealed class ConstantHeightProvider : IHeightProvider
{
    public string Type => "minecarft:constant";

    public int? Absolute { get; init; }
    public int? AboveBottom { get; init; }
    public int? BelowTop { get; init; }

    public required VerticalAnchor Value { get; init; }
}
