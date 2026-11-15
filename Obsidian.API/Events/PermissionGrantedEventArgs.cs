namespace Obsidian.API.Events;

/// <summary>
/// An explicit permission path added to a player, raised after it is added and saved. It isn't raised if the path
/// already exists. A wildcard may already have granted access before this event.
/// </summary>
public class PermissionGrantedEventArgs : PlayerEventArgs
{
    /// <summary>
    /// Gets the permission node that was granted, as passed to <see cref="IPlayer.GrantPermissionAsync(string)"/>.
    /// </summary>
    public string Permission { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionGrantedEventArgs"/> class.
    /// </summary>
    /// <param name="player">The player who was granted the permission.</param>
    /// <param name="server">The server the event takes place in.</param>
    /// <param name="permission">The permission node that was granted.</param>
    public PermissionGrantedEventArgs(IPlayer player, IServer server, string permission) : base(player, server)
    {
        Permission = permission;
    }
}
