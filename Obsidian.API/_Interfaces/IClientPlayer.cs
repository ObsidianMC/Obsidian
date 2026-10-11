namespace Obsidian.API;

/// <summary>
/// A player that is backed by an active Minecraft client connection.
/// </summary>
public interface IClientPlayer : IPlayer
{
    public IClient Client { get; }
}
