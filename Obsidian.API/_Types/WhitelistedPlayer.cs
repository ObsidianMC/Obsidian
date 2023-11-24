namespace Obsidian.API;
public readonly struct WhitelistedPlayer
{
    public required string Name { get; init; }
    public required Guid Id { get; init; }
}
