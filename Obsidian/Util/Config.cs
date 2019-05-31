using Newtonsoft.Json;

namespace Obsidian.Entities
{
    public class Config
    {
        [JsonProperty("motd")]
        public string Motd = "§dObsidian §rv§c0.1§a-DEVEL\n§r§lRunning on .NET Core 2.1 <3";

        [JsonProperty("port")]
        public int Port = 25565;

        [JsonProperty("server-count")]
        public int ServerCount = 1; // for now this does nothing. every server port will be port + n

        [JsonProperty("joinmessage")]
        public string JoinMessage = "Suck the bbq sauce on my §d§ltiddies"; // Message on join, because we haven't implemented the actual server yet.

        [JsonProperty("online_mode")]
        public bool OnlineMode = true;

    }
}
