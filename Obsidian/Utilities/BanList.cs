using Microsoft.Extensions.Logging;
using System.IO;

namespace Obsidian.Utilities;

public class BanList : IBanList
{
    public IList<IBan> Bans { get { return bans; } }
    private List<IBan> bans { get; set;}
    private readonly Server server;
    private string Path => System.IO.Path.Combine(this.server.ServerFolderPath, "bans.json");

    public BanList(Server server)
    {
        this.bans = new List<IBan>();
        this.server = server;
    }

    public async Task InitializeAsync()
    {
        var fi = new FileInfo(this.Path);

        if (fi.Exists)
        {
            using var fs = fi.OpenRead();
            this.bans = await fs.FromJsonAsync<List<IBan>>();
            foreach (Ban ban in bans)
            {
                if (DateTime.Now.CompareTo(ban.Duration) <= 0)
                {
                    bans.Remove(ban);
                    break;
                }
            }
            fs.Close();
            UpdateList();
        }
        else
        {
            using var fs = fi.Create();

            await this.bans.ToJsonAsync(fs);
        }
    }

    public void AddBan(IPlayer player, TimeSpan? duration)
    {
        if (duration is null)
        {
            bans.Add(new Ban { Username = player.Username, Uuid = player.Uuid, Duration = null, TimeStamp = DateTime.Now });
        }
        else
        {
            bans.Add(new Ban { Username = player.Username, Uuid = player.Uuid, Duration = duration, TimeStamp = DateTime.Now });
        }

        this.UpdateList();
    }

    public void AddBan(string username, TimeSpan? duration)
    {
        if (duration is null)
        {
            this.bans.Add(new Ban { Username = username, Uuid = Guid.Empty, Duration = null, TimeStamp = DateTime.Now });
        }
        else
        {
            this.bans.Add(new Ban { Username = username, Uuid = Guid.Empty, Duration = duration, TimeStamp = DateTime.Now });
        }

        this.UpdateList();
    }

    public void RemoveBan(IPlayer player)
    {
        this.bans.RemoveAll(x => x.Uuid == player.Uuid || x.Username == player.Username);
        this.UpdateList();
    }

    public void RemoveBan(string username)
    {
        this.bans.RemoveAll(x => x.Username == username || x.Uuid == Guid.Parse(username));
        this.UpdateList();
    }

    public IBan GetBan(IPlayer player)
    {
        return this.bans.FirstOrDefault(ban => ban.Username == player.Username);
    }

    public IBan GetBan(string username)
    {
        return this.bans.FirstOrDefault(ban => ban.Username == username);
    }

    public bool IsBanned(IPlayer player) => this.bans.Any(x => x.Username == player.Username || player.Uuid == x.Uuid);
    public bool IsBanned(string username) => this.bans.Any(x => x.Username == username || x.Uuid == Guid.Parse(username));

    private void UpdateList()
    {
        File.WriteAllText(Path, bans.ToJson());
    }

    private class Ban : IBan
    {
        public string Username { get; set; }
        public Guid Uuid { get; set; }
        public TimeSpan? Duration { get; set; }
        public DateTime TimeStamp { get; set; }
    }
}
