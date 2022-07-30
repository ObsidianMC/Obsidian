namespace Obsidian.API.Events;

public sealed class PermissionGrantedEventArgs : BaseEventArgs, IPlayerEvent
{
    internal PermissionGrantedEventArgs(IPlayer player, string permission)
    {
        Player = player;
        Permission = permission;
    }

    /// <summary>
    /// The granted permission
    /// </summary>
    public string Permission { get; }

    public IPlayer Player { get; }
}
