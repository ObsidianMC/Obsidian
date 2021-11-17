namespace Obsidian.API.Events;

public class PermissionGrantedEventArgs : PlayerEventArgs
{
    public string Permission { get; }

    public PermissionGrantedEventArgs(IPlayer player, string permission) : base(player)
    {
        Permission = permission;
    }
}
