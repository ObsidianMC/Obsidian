namespace Obsidian.API.Events;

/// <summary>
/// A permission revoked from a player, raised after it and the permissions under it are removed and saved. It isn't
/// raised when the player didn't have the permission.
/// </summary>
public class PermissionRevokedEventArgs : PlayerEventArgs
{
    /// <summary>
    /// Gets the permission node that was revoked, as passed to <see cref="IPlayer.RevokePermissionAsync(string)"/>.
    /// </summary>
    public string Permission { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionRevokedEventArgs"/> class.
    /// </summary>
    /// <param name="player">The player whose permission was revoked.</param>
    /// <param name="server">The server the event takes place in.</param>
    /// <param name="permission">The permission node that was revoked.</param>
    public PermissionRevokedEventArgs(IPlayer player, IServer server, string permission) : base(player, server)
    {
        Permission = permission;
    }
}
