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
    }
}
