namespace Obsidian.API;

public interface IBanList
{
    public List<IBan> Bans { get; set;}
    public void AddBan(IPlayer player, int? duration);
    public void AddBan(string username, int? duration);
    public void RemoveBan(IPlayer player);
    public void RemoveBan(string username);
    public IBan GetBan(IPlayer player);
    public IBan GetBan(string username);
    public bool IsBanned(IPlayer player);
    public bool IsBanned(string username);
}
