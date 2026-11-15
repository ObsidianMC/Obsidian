namespace Obsidian.API;

/// <summary>
/// A player that is backed by an active Minecraft client connection.
/// </summary>
public interface IClientPlayer : IPlayer
{
    /// <summary>
    /// Gets the player's connection.
    /// </summary>
    public IClient Client { get; }
}
