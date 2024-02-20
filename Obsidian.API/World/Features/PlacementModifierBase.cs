namespace Obsidian.API.World.Features;
public abstract class PlacementModifierBase
{
    public abstract string Type { get; internal init; }

    public Vector[] GetPositions(PlacementContext context) => [];

    protected abstract bool ShouldPlace(PlacementContext context);
}

public readonly struct PlacementContext
{
    public required IWorld World { get; init; }

    public required IServer Server { get; init; }

    public required Vector BlockPosition { get; init; }
}
