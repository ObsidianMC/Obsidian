namespace Obsidian.API;
public readonly record struct WhitelistedPlayer
{
    public required string Name { get; init; }
    public required Guid Id { get; init; }
}
