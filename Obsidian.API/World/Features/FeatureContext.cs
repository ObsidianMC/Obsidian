namespace Obsidian.API.World.Features;

public sealed class FeatureContext
{
    public required IWorld World { get; init; } = default!;

    public required Vector PlacementLocation { get; init; } = default!;

    public required Random Random { get; init; } = default!;

    internal FeatureContext() { }
}
