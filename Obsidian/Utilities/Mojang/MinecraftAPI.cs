using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Obsidian.Utilities.Mojang
{
    public class MinecraftAPI
    {
        private static readonly HttpClient Http = Globals.HttpClient;

        public static async Task<List<MojangUser>> GetUsersAsync(string[] usernames)
        {
            using HttpResponseMessage response = await Http.PostAsync("https://api.mojang.com/profiles/minecraft", new StringContent(JsonSerializer.Serialize(usernames), Encoding.UTF8, "application/json"));

            return response.IsSuccessStatusCode ? (await response.Content.ReadAsStringAsync()).FromJson<List<MojangUser>>() : null;
        }

        public static async Task<MojangUser> GetUserAsync(string username)
        {
            var users = await GetUsersAsync(new[] { username });

            return (users == null || users.Count <= 0) ? null : users.FirstOrDefault();
        }

        public static async Task<MojangUser> GetUserAndSkinAsync(string uuid)
        {
            using HttpResponseMessage response = await Http.GetAsync("https://sessionserver.mojang.com/session/minecraft/profile/" + uuid);

            return response.IsSuccessStatusCode ? (await response.Content.ReadAsStringAsync()).FromJson<MojangUser>() : null;
        }

        public static async Task<JoinedResponse> HasJoined(string username, string serverId)
        {
            using HttpResponseMessage response = await Http.GetAsync($"https://sessionserver.mojang.com/session/minecraft/hasJoined?username={username}&serverId={serverId}");

            return response.IsSuccessStatusCode ? (await response.Content.ReadAsStringAsync()).FromJson<JoinedResponse>() : null;
        }
    }
}
