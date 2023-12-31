namespace Obsidian.API.Events;

public class PermissionRevokedEventArgs : PlayerEventArgs
{
    public static new string Name => "PermissionRevoked";

    public string Permission { get; }

    public PermissionRevokedEventArgs(IPlayer player, IServer server, string permission) : base(player, server)
    {
        Permission = permission;
    }
}
