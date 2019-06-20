using Newtonsoft.Json;
using Obsidian.Util.Converters;

namespace Obsidian.Entities
{
    public class Config
    {
        [JsonProperty("motd")]
        public string Motd = "§dObsidian §rv§c0.1§a-DEVEL\n§r§lRunning on .NET Core 2.1 <3";

        [JsonProperty("port")]
        public int Port = 25565;

        [JsonProperty("serverCount")]
        public int ServerCount = 1;

        [JsonProperty("generator"), JsonConverter(typeof(GeneratorConverter))]
        public Generator Generator = Generator.Normal;

        [JsonProperty("joinMessage")]
        public string JoinMessage = "§e{0} joined the game";

        [JsonProperty("leaveMessage")]
        public string LeaveMessage = "§e{0} left the game";

        [JsonProperty("onlineMode")]
        public bool OnlineMode = true;

        [JsonProperty("debugMode")]
        public bool DebugMode = false;
    }

    public enum Generator
    {
        Normal,

        Superflat,

        Void
    }
}
