using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Web;

namespace Obsidian.Utilities.Mojang;

public class MinecraftAPI
{
    private static readonly HttpClient httpClient = new();

    public static async Task<List<MojangUser>?> GetUsersAsync(params string[] usernames)
    {
        List<string> escapedUsernames = new();
        for (int i = 0; i < usernames.Length; i++)
        {
            escapedUsernames.Add(HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(usernames[i])));
        }

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("https://api.mojang.com/profiles/minecraft", escapedUsernames);
        return response.IsSuccessStatusCode ? (await response.Content.ReadFromJsonAsync<List<MojangUser>>()) : null;
    }

    public static async Task<MojangUser?> GetUserAsync(string username)
    {
        string escapedUsername = HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(username));
        List<MojangUser>? users = await GetUsersAsync(escapedUsername);

        return (users == null || users.Count <= 0) ? null : users.FirstOrDefault();
    }

    public static async Task<MojangUser?> GetUserAndSkinAsync(string uuid)
    {
        string escapedUuid = HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(uuid));
        return await httpClient.GetFromJsonAsync<MojangUser>($"https://sessionserver.mojang.com/session/minecraft/profile/{escapedUuid}");
    }

    public static async Task<JoinedResponse?> HasJoined(string username, string serverId)
    {
        string escapedUsername = HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(username));
        string escapedServerId = HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(serverId));
        return await httpClient.GetFromJsonAsync<JoinedResponse>($"https://sessionserver.mojang.com/session/minecraft/hasJoined?username={escapedUsername}&serverId={escapedServerId}");
    }
}
