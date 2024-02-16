namespace Obsidian.API.World.Features;
public interface IHeightProvider
{
    public string Type { get; }
    public int? Absolute { get; init; }

    public int? AboveBottom { get; init; }

    public int? BelowTop { get; init; }
}
