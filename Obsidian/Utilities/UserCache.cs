using Microsoft.CodeAnalysis.CSharp.Syntax;
using Obsidian.Utilities.Mojang;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Web;

namespace Obsidian.Utilities;

public static class UserCache
{
    private const string userWithNameEndpoint = "https://api.mojang.com/users/profiles/minecraft/";
    private const string userWithIdEndpoint = "https://sessionserver.mojang.com/session/minecraft/profile/";
    private const string verifySessionEndpoint = "https://sessionserver.mojang.com/session/minecraft/hasJoined";

    private static readonly HttpClient httpClient = Globals.HttpClient;

    private static ConcurrentDictionary<Guid, CachedUser> cache = new();

    private static readonly FileInfo cacheFile = new("usercache.json");

    public static async Task<CachedUser?> GetUserFromNameAsync(string username)
    {
        var escapedUsername = Sanitize(username);

        CachedUser? cachedUser = null;
        if (cache.Any(x => x.Value.Name == username))
        {
            cachedUser = cache.First(x => x.Value.Name == username).Value;

            if (!cachedUser.Expired)
                return cachedUser;
        }

        var user = await httpClient.GetFromJsonAsync<MojangUser>($"{userWithNameEndpoint}{escapedUsername}", Globals.JsonOptions);

        if (user is null)
            return null;

        if (cache.TryGetValue(user.Id, out cachedUser))
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

        cache.TryAdd(cachedUser.Id, cachedUser);

        return cachedUser;
    }

    public static async Task<CachedUser> GetUserFromUuidAsync(Guid uuid)
    {
        if (cache.TryGetValue(uuid, out var user) && !user.Expired)
            return user;

        var escapedUuid = Sanitize(uuid.ToString("N"));

        var mojangUser = await httpClient.GetFromJsonAsync<MojangUser>($"{userWithIdEndpoint}{escapedUuid}", Globals.JsonOptions);

        if (mojangUser is null)
            throw new UnreachableException();//This isn't supposed to happen

        user = new()
        {
            Name = mojangUser!.Name,
            Id = uuid,
            ExpiresOn = DateTimeOffset.UtcNow.AddMonths(1)
        };

        cache.TryAdd(uuid, user);

        return user;
    }

    public static async Task<MojangUser?> HasJoinedAsync(string username, string serverId)
    {
        var escapedUsername = Sanitize(username);
        var escapedServerId = Sanitize(serverId);

        return await httpClient.GetFromJsonAsync<MojangUser>($"{verifySessionEndpoint}?username={escapedUsername}&serverId={escapedServerId}", Globals.JsonOptions);
    }

    public static async Task SaveAsync()
    {
        await using var sw = cacheFile.Open(FileMode.Truncate, FileAccess.Write);

        await JsonSerializer.SerializeAsync(sw, cache, Globals.JsonOptions);

        await sw.FlushAsync();
    }

    public static async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var sr = cacheFile.Open(FileMode.OpenOrCreate, FileAccess.Read);

        if (sr.Length == 0)
            return;

        var userCache = await JsonSerializer.DeserializeAsync<Dictionary<Guid, CachedUser>>(sr, Globals.JsonOptions, cancellationToken);

        if (userCache is null)
            return;

        cache = new(userCache);
    }

    private static string Sanitize(string value) => HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(value));
}
