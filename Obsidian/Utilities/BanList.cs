using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading;

namespace Obsidian.Utilities;

/// <summary>
/// The players and IP addresses banned from the server, kept in <c>config/banned-players.json</c> and
/// <c>config/banned-ips.json</c> like vanilla's lists. An integrated server keeps them in memory only.
/// </summary>
public sealed partial class BanList(ILogger<BanList> logger, bool persistent = true)
{
    private static string PlayersFilePath => Path.Combine(ServerConstants.ConfigPath, "banned-players.json");
    private static string IpsFilePath => Path.Combine(ServerConstants.ConfigPath, "banned-ips.json");

    private readonly Lock gate = new();
    private readonly SemaphoreSlim saving = new(1, 1);

    private List<BannedPlayer> players = [];
    private List<BannedIp> ips = [];

    public IReadOnlyList<BannedPlayer> Players
    {
        get
        {
            lock (this.gate)
                return [.. this.players];
        }
    }

    public IReadOnlyList<BannedIp> Ips
    {
        get
        {
            lock (this.gate)
                return [.. this.ips];
        }
    }

    public async Task InitializeAsync()
    {
        if (!persistent)
            return;

        var players = await ReadAsync<BannedPlayer>(PlayersFilePath);
        var ips = await ReadAsync<BannedIp>(IpsFilePath);

        lock (this.gate)
        {
            this.players = players;
            this.ips = ips;
        }
    }

    /// <summary>The ban on a player, by UUID or, for players banned by name only, by name.</summary>
    public BannedPlayer? GetBan(Guid uuid, string username)
    {
        lock (this.gate)
        {
            return this.players.FirstOrDefault(ban => ban.Uuid is Guid id && id == uuid && uuid != Guid.Empty
                || string.Equals(ban.Name, username, StringComparison.OrdinalIgnoreCase));
        }
    }

    public BannedIp? GetBan(string ip)
    {
        lock (this.gate)
            return this.ips.FirstOrDefault(ban => ban.Ip == ip);
    }

    /// <summary>Bans a player, returning <see langword="false"/> if they already were.</summary>
    public bool Ban(string name, Guid? uuid, string source, string? reason)
    {
        lock (this.gate)
        {
            if (this.players.Any(ban => uuid is Guid id && ban.Uuid == id || string.Equals(ban.Name, name, StringComparison.OrdinalIgnoreCase)))
                return false;

            this.players.Add(new BannedPlayer(uuid, name, DateTimeOffset.UtcNow, source, reason ?? "Banned by an operator."));
        }

        this.Save();
        return true;
    }

    public bool Pardon(string name)
    {
        bool removed;
        lock (this.gate)
            removed = this.players.RemoveAll(ban => string.Equals(ban.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

        if (removed)
            this.Save();

        return removed;
    }

    public bool BanIp(string ip, string source, string? reason)
    {
        lock (this.gate)
        {
            if (this.ips.Any(ban => ban.Ip == ip))
                return false;

            this.ips.Add(new BannedIp(ip, DateTimeOffset.UtcNow, source, reason ?? "Banned by an operator."));
        }

        this.Save();
        return true;
    }

    public bool PardonIp(string ip)
    {
        bool removed;
        lock (this.gate)
            removed = this.ips.RemoveAll(ban => ban.Ip == ip) > 0;

        if (removed)
            this.Save();

        return removed;
    }

    /// <summary>The message a banned player is disconnected with.</summary>
    public static ChatMessage DisconnectMessage(string reason) =>
        ChatMessage.Simple($"You are banned from this server.\nReason: {reason}");

    private void Save()
    {
        if (!persistent)
            return;

        _ = this.SaveAsync();
    }

    private async Task SaveAsync()
    {
        await this.saving.WaitAsync();
        try
        {
            await WriteAsync(PlayersFilePath, this.Players);
            await WriteAsync(IpsFilePath, this.Ips);
        }
        catch (IOException ex)
        {
            Log.SaveFailed(logger, ex);
        }
        finally
        {
            this.saving.Release();
        }
    }

    private static async Task<List<TEntry>> ReadAsync<TEntry>(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
            return [];

        await using var stream = file.OpenRead();
        return await stream.FromJsonAsync<List<TEntry>>() ?? [];
    }

    private static async Task WriteAsync<TEntry>(string path, IReadOnlyList<TEntry> entries)
    {
        var file = new FileInfo(path);
        file.Directory?.Create();

        await using var stream = file.Create();
        await entries.ToJsonAsync(stream);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't save the ban list")]
        public static partial void SaveFailed(ILogger logger, Exception exception);
    }
}

/// <param name="Uuid">The player's UUID, unknown for players banned by name before they joined.</param>
public sealed record BannedPlayer(Guid? Uuid, string Name, DateTimeOffset Created, string Source, string Reason);

public sealed record BannedIp(string Ip, DateTimeOffset Created, string Source, string Reason);
