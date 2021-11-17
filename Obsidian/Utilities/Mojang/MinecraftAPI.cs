using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Obsidian.Utilities.Mojang;

public class MinecraftAPI
{
    private static readonly HttpClient Http = Globals.HttpClient;

    public static async Task<List<MojangUser>> GetUsersAsync(string[] usernames)
    {
        var escaped_usernames = new List<string>();

        for (int i = 0; i < usernames.Length; i++)
            escaped_usernames.Add(HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(usernames[i])));

        using HttpResponseMessage response = await Http.PostAsync("https://api.mojang.com/profiles/minecraft", new StringContent(JsonSerializer.Serialize(escaped_usernames), Encoding.UTF8, "application/json"));

        return response.IsSuccessStatusCode ? (await response.Content.ReadAsStringAsync()).FromJson<List<MojangUser>>() : null;
    }

    public static async Task<MojangUser> GetUserAsync(string username)
    {
        var escaped_username = HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(username));
        var users = await GetUsersAsync(new[] { escaped_username });

        return (users == null || users.Count <= 0) ? null : users.FirstOrDefault();
    }

    public static async Task<MojangUser> GetUserAndSkinAsync(string uuid)
    {
        var escaped_uuid = HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(uuid));
        // I'm fairly sure this is not exploitable, but good practice and consistency are two important factors imo
        using HttpResponseMessage response = await Http.GetAsync("https://sessionserver.mojang.com/session/minecraft/profile/" + escaped_uuid);

        return response.IsSuccessStatusCode ? (await response.Content.ReadAsStringAsync()).FromJson<MojangUser>() : null;
    }

    public static async Task<JoinedResponse> HasJoined(string username, string serverId)
    {
        var escaped_username = HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(username));
        var escaped_serverid = HttpUtility.UrlEncode(Encoding.UTF8.GetBytes(serverId));
        using HttpResponseMessage response = await Http.GetAsync($"https://sessionserver.mojang.com/session/minecraft/hasJoined?username={escaped_username}&serverId={escaped_serverid}");

        return response.IsSuccessStatusCode ? (await response.Content.ReadAsStringAsync()).FromJson<JoinedResponse>() : null;
    }
}
