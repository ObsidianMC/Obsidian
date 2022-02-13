namespace Obsidian.API;

public interface IBanList
{
    public IList<IBan> Bans { get; }
    public void AddBan(IPlayer player, TimeSpan? duration);
    public void AddBan(string username, TimeSpan? duration);
    public void RemoveBan(IPlayer player);
    public void RemoveBan(string username);
    public IBan GetBan(IPlayer player);
    public IBan GetBan(string username);
    public bool IsBanned(IPlayer player);
    public bool IsBanned(string username);
}
