using Newtonsoft.Json;

namespace Obsidian.Entities
{
    public class Config
    {
        [JsonProperty("motd")]
        public string Motd = "§dObsidian §rv§c0.1§a-DEVEL\n§r§lRunning on .NET Core 2.1 <3";

        [JsonProperty("port")]
        public int Port = 25565;

        [JsonProperty("serverCount")]
        public int ServerCount = 1; // for now this does nothing. every server port will be port + n

        [JsonProperty("joinMessage")]
        public string JoinMessage = "§e{0} joined the game";

        [JsonProperty("leaveMessage")]
        public string LeaveMessage = "§e{0} left the game";

        [JsonProperty("onlineMode")]
        public bool OnlineMode = true;

        [JsonProperty("debugMode")]
        public bool DebugMode = false;
    }
}
