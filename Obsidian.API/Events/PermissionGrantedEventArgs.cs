namespace Obsidian.API.Events;

public class PermissionGrantedEventArgs : PlayerEventArgs
{
    public override string Name => "PermissionGranted";

    public string Permission { get; }

    public PermissionGrantedEventArgs(IPlayer player, IServer server, string permission) : base(player, server)
    {
        Permission = permission;
    }
}
