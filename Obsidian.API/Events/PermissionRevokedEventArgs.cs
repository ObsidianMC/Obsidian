namespace Obsidian.API.Events;

public class PermissionRevokedEventArgs : PlayerEventArgs
{
    public string Permission { get; }

    public PermissionRevokedEventArgs(IPlayer player, string permission) : base(player)
    {
        Permission = permission;
    }
}
