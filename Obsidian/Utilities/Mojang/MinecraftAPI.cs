using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SharpNoise.Modules;

namespace Obsidian.Utilities.Mojang
{
    public class MinecraftAPI
    {
        private readonly HttpClient httpClient;

        public MinecraftAPI(IHttpClientFactory httpClientFactory)
        {
            httpClient = httpClientFactory.CreateClient(nameof(MinecraftAPI));
        }

        public async Task<List<MojangUser>> GetUsersAsync(string[] usernames)
        {
            using (HttpResponseMessage response = await httpClient.PostAsync("https://api.mojang.com/profiles/minecraft", new StringContent(JsonConvert.SerializeObject(usernames), Encoding.UTF8, "application/json")))
            {
                if (response.IsSuccessStatusCode)
                {
                    return JsonConvert.DeserializeObject<List<MojangUser>>(await response.Content.ReadAsStringAsync());
                }
            }

            return null;
        }

        public async Task<MojangUser> GetUserAsync(string username)
        {
            var users = await GetUsersAsync(new[] { username });

            if (users is not {Count: > 0})
                return null;

            return users.FirstOrDefault();
        }

        public async Task<MojangUser> GetUserAndSkinAsync(string uuid)
        {
            using (HttpResponseMessage response = await httpClient.GetAsync("https://sessionserver.mojang.com/session/minecraft/profile/" + uuid))
            {
                if (response.IsSuccessStatusCode)
                {
                    return JsonConvert.DeserializeObject<MojangUser>(await response.Content.ReadAsStringAsync());
                }
            }

            return null;
        }

        public static async Task<JoinedResponse> HasJoined(string username, string serverId)
        {
            using (HttpResponseMessage response = await httpClient.GetAsync($"https://sessionserver.mojang.com/session/minecraft/hasJoined?username={username}&serverId={serverId}"))
            {
                if (response.IsSuccessStatusCode)
                {
                    return JsonConvert.DeserializeObject<JoinedResponse>(await response.Content.ReadAsStringAsync());
                }
            }

            return null;
        }
    }
}
