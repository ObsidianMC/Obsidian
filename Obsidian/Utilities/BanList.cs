using Microsoft.Extensions.Logging;
using System.IO;

namespace Obsidian.Utilities;

public class BanList : IBanList
{
    public List<IBan> Bans { get; set;}
    private readonly Server server;
    private string Path => System.IO.Path.Combine(this.server.ServerFolderPath, "bans.json");

    public BanList(Server server)
    {
        this.Bans = new List<IBan>();
        this.server = server;
    }

    public async Task InitializeAsync()
    {
        var fi = new FileInfo(this.Path);

        if (fi.Exists)
        {
            using var fs = fi.OpenRead();
            this.Bans = await fs.FromJsonAsync<List<IBan>>();
            foreach (Ban ban in Bans)
            {
                if (DateTime.Now.CompareTo(ban.Duration) <= 0)
                {
                    Bans.Remove(ban);
                    break;
                }
            }
            fs.Close();
            UpdateList();
        }
        else
        {
            using var fs = fi.Create();

            await this.Bans.ToJsonAsync(fs);
        }
    }

    public void AddBan(IPlayer player, int? duration)
    {
        if (duration is null)
        {
            Bans.Add(new Ban { Username = player.Username, Uuid = player.Uuid, Duration = null, TimeStamp = DateTime.Now });
        }
        else
        {
            Bans.Add(new Ban { Username = player.Username, Uuid = player.Uuid, Duration = new TimeSpan((int) duration, 0, 0, 0), TimeStamp = DateTime.Now });
        }

        this.UpdateList();
    }

    public void AddBan(string username, int? duration)
    {
        if (duration is null)
        {
            this.Bans.Add(new Ban { Username = username, Uuid = Guid.Empty, Duration = null, TimeStamp = DateTime.Now });
        }
        else
        {
            this.Bans.Add(new Ban { Username = username, Uuid = Guid.Empty, Duration = new TimeSpan((int) duration, 0, 0, 0), TimeStamp = DateTime.Now });
        }

        this.UpdateList();
    }

    public void RemoveBan(IPlayer player)
    {
        this.Bans.RemoveAll(x => x.Uuid == player.Uuid || x.Username == player.Username);
        this.UpdateList();
    }

    public void RemoveBan(string username)
    {
        this.Bans.RemoveAll(x => x.Username == username || x.Uuid == Guid.Parse(username));
        this.UpdateList();
    }

    public IBan GetBan(IPlayer player)
    {
        return this.Bans.FirstOrDefault(ban => ban.Username == player.Username);
    }

    public IBan GetBan(string username)
    {
        return this.Bans.FirstOrDefault(ban => ban.Username == username);
    }

    public bool IsBanned(IPlayer player) => this.Bans.Any(x => x.Username == player.Username || player.Uuid == x.Uuid);
    public bool IsBanned(string username) => this.Bans.Any(x => x.Username == username || x.Uuid == Guid.Parse(username));

    private void UpdateList()
    {
        File.WriteAllText(Path, Bans.ToJson());
    }

    private class Ban : IBan
    {
        public string Username { get; set; }
        public Guid Uuid { get; set; }
        public TimeSpan? Duration { get; set; }
        public DateTime TimeStamp { get; set; }
    }
}
