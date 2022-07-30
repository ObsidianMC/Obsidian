namespace Obsidian.API.Events;

public sealed class PermissionRevokedEventArgs : BaseEventArgs, IPlayerEvent
{
    internal PermissionRevokedEventArgs(IPlayer player, string permission)
    {
        Player = player;
        Permission = permission;
    }

    /// <summary>
    /// The revoked permission
    /// </summary>
    public string Permission { get; }

    public IPlayer Player { get; }
}
