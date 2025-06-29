using Microsoft.Extensions.Logging;
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
public sealed class UserCache(HttpClient httpClient, ILogger<UserCache> logger) : IUserCache
{
    private const string userWithNameEndpoint = "https://api.mojang.com/users/profiles/minecraft/";
    private const string userWithIdEndpoint = "https://sessionserver.mojang.com/session/minecraft/profile/";
    private const string verifySessionEndpoint = "https://sessionserver.mojang.com/session/minecraft/hasJoined";

    private readonly FileInfo cacheFile = new("usercache.json");
    private readonly ILogger<UserCache> logger = logger;

    private List<CachedProfile> cachedUsers = new();

    public async ValueTask<CachedProfile?> GetCachedUserFromNameAsync(string username)
    {
        var escapedUsername = Sanitize(username);

        var cachedUser = this.cachedUsers.FirstOrDefault(x => x.Name == username);
        if (cachedUser != null && !cachedUser.Expired)
            return cachedUser;

        try
        {
            var user = await httpClient.GetFromJsonAsync<MojangProfile>($"{userWithNameEndpoint}{escapedUsername}", Globals.JsonOptions);

            cachedUser = new()
            {
                Uuid = user.Id,
                Name = user.Name,
                ExpiresOn = DateTimeOffset.UtcNow.AddMonths(1)
            };

            cachedUsers.Add(cachedUser);

            return cachedUser;
        }
        catch
        {
            logger.LogWarning("Could not get user profile for {Username}", username);
            return null;
        }
    }

    public async ValueTask<CachedProfile> GetCachedUserFromUuidAsync(Guid uuid)
    {
        var cachedUser = this.cachedUsers.FirstOrDefault(x => x.Uuid == uuid);
        if (cachedUser != null && !cachedUser.Expired)
            return cachedUser;

        var escapedUuid = Sanitize(uuid.ToString("N"));

        var mojangProfile = await httpClient.GetFromJsonAsync<MojangProfile>($"{userWithIdEndpoint}{escapedUuid}", Globals.JsonOptions) ?? throw new UnreachableException();

        cachedUser = new()
        {
            Name = mojangProfile!.Name,
            Uuid = uuid,
            ExpiresOn = DateTimeOffset.UtcNow.AddMonths(1)
        };

        cachedUsers.Add(cachedUser);

        return cachedUser;
    }

    public async Task<MojangProfile?> HasJoinedAsync(string username, string serverId)
    {
        var escapedUsername = Sanitize(username);
        var escapedServerId = Sanitize(serverId);

        return await httpClient.GetFromJsonAsync<MojangProfile>($"{verifySessionEndpoint}?username={escapedUsername}&serverId={escapedServerId}", Globals.JsonOptions);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await using var sw = cacheFile.Open(FileMode.Truncate, FileAccess.Write);

        await JsonSerializer.SerializeAsync(sw, cachedUsers, Globals.JsonOptions, cancellationToken);

        await sw.FlushAsync(cancellationToken);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var sr = cacheFile.Open(FileMode.OpenOrCreate, FileAccess.Read);

        if (sr.Length == 0)
            return;

        var userCache = await JsonSerializer.DeserializeAsync<List<CachedProfile>>(sr, Globals.JsonOptions, cancellationToken);
        if (userCache is null)
            return;

        cachedUsers = new(userCache);
    }

    private static string Sanitize(string value) => HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(value));
}

public interface IUserCache
{
    public ValueTask<CachedProfile?> GetCachedUserFromNameAsync(string username);

    public ValueTask<CachedProfile> GetCachedUserFromUuidAsync(Guid uuid);

    public Task<MojangProfile?> HasJoinedAsync(string username, string serverId);

    public Task SaveAsync(CancellationToken cancellationToken = default);

    public Task LoadAsync(CancellationToken cancellationToken = default);
}
