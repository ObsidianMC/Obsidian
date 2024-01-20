namespace Obsidian.API.Events;

public class PermissionRevokedEventArgs : PlayerEventArgs
{
    public override string Name => "PermissionRevoked";

    public string Permission { get; }

    public PermissionRevokedEventArgs(IPlayer player, IServer server, string permission) : base(player, server)
    {
        Permission = permission;
    }
}
