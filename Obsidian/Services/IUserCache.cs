using Obsidian.Entities;
using Obsidian.Utilities.Mojang;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Web;

namespace Obsidian.Services;
public sealed class UserCache(HttpClient httpClient) : IUserCache
{
    private const string userWithNameEndpoint = "https://api.mojang.com/users/profiles/minecraft/";
    private const string userWithIdEndpoint = "https://sessionserver.mojang.com/session/minecraft/profile/";
    private const string verifySessionEndpoint = "https://sessionserver.mojang.com/session/minecraft/hasJoined";

    private readonly FileInfo cacheFile = new("usercache.json");

    private ConcurrentDictionary<Guid, CachedUser> cachedUsers;

    public ConcurrentDictionary<Guid, Player> OnlinePlayers { get; } = new ();

    public async Task<CachedUser?> GetCachedUserFromNameAsync(string username)
    {
        var escapedUsername = Sanitize(username);

        CachedUser? cachedUser;
        if (cachedUsers.Any(x => x.Value.Name == username))
        {
            cachedUser = cachedUsers.First(x => x.Value.Name == username).Value;

            if (!cachedUser.Expired)
                return cachedUser;
        }

        var user = await httpClient.GetFromJsonAsync<MojangUser>($"{userWithNameEndpoint}{escapedUsername}", Globals.JsonOptions);

        if (user is null)
            return null;

        if (cachedUsers.TryGetValue(user.Id, out cachedUser))
        {
            if (cachedUser.Expired)
            {
                cachedUser.ExpiresOn = DateTimeOffset.UtcNow.AddMonths(1);
                cachedUser.Name = user.Name;
            }

            return cachedUser;
        }

        cachedUser = new()
        {
            Id = user.Id,
            Name = user.Name,
            ExpiresOn = DateTimeOffset.UtcNow.AddMonths(1)
        };

        cachedUsers.TryAdd(cachedUser.Id, cachedUser);

        return cachedUser;
    }

    public async Task<CachedUser> GetCachedUserFromUuidAsync(Guid uuid)
    {
        if (cachedUsers.TryGetValue(uuid, out var user) && !user.Expired)
            return user;

        var escapedUuid = Sanitize(uuid.ToString("N"));

        var mojangUser = await httpClient.GetFromJsonAsync<MojangUser>($"{userWithIdEndpoint}{escapedUuid}", Globals.JsonOptions) ?? throw new UnreachableException();
        user = new()
        {
            Name = mojangUser!.Name,
            Id = uuid,
            ExpiresOn = DateTimeOffset.UtcNow.AddMonths(1)
        };

        cachedUsers.TryAdd(uuid, user);

        return user;
    }

    public async Task<MojangUser?> HasJoinedAsync(string username, string serverId)
    {
        var escapedUsername = Sanitize(username);
        var escapedServerId = Sanitize(serverId);

        return await httpClient.GetFromJsonAsync<MojangUser>($"{verifySessionEndpoint}?username={escapedUsername}&serverId={escapedServerId}", Globals.JsonOptions);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await using var sw = cacheFile.Open(FileMode.Truncate, FileAccess.Write);

        await JsonSerializer.SerializeAsync(sw, cachedUsers, Globals.JsonOptions);

        await sw.FlushAsync();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var sr = cacheFile.Open(FileMode.OpenOrCreate, FileAccess.Read);

        if (sr.Length == 0)
            return;

        var userCache = await JsonSerializer.DeserializeAsync<Dictionary<Guid, CachedUser>>(sr, Globals.JsonOptions, cancellationToken);

        if (userCache is null)
            return;

        cachedUsers = new(userCache);
    }

    private static string Sanitize(string value) => HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(value));
}

public interface IUserCache
{
    public ConcurrentDictionary<Guid, Player> OnlinePlayers { get; }

    public Task<CachedUser?> GetCachedUserFromNameAsync(string username);

    public Task<CachedUser> GetCachedUserFromUuidAsync(Guid uuid);

    public Task<MojangUser?> HasJoinedAsync(string username, string serverId);

    public Task SaveAsync(CancellationToken cancellationToken = default);

    public Task LoadAsync(CancellationToken cancellationToken = default);
}
