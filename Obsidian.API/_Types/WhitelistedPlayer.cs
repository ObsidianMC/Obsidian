namespace Obsidian.API;
/// <summary>A player on the server's whitelist.</summary>
public readonly record struct WhitelistedPlayer
{
    /// <summary>Gets the player's username.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the player's UUID.</summary>
    public required Guid Id { get; init; }
}
