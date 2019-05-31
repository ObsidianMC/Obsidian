using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Util
{
    public class MinecraftAPI
    {
        public static HttpClient Http = new HttpClient();


        public static async Task<List<MojangUser>> GetUsersAsync(string[] usernames)
        {
            HttpResponseMessage response = await Http.PostAsync("https://api.mojang.com/profiles/minecraft", new StringContent(JsonConvert.SerializeObject(usernames), Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<List<MojangUser>>(await response.Content.ReadAsStringAsync());
            }

            return null;
        }

        //TODO Uh find a good name for name
        public static async Task<JoinedResponse> HasJoined(string username, string serverId)
        {
            HttpResponseMessage response = await Http.GetAsync($"https://sessionserver.mojang.com/session/minecraft/hasJoined?username={username}&serverId={serverId}");

            if (response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<JoinedResponse>(await response.Content.ReadAsStringAsync());
            }

            return null;
        }
    }
}
