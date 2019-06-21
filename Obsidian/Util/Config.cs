using Newtonsoft.Json;
using Obsidian.Logging;

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

        [JsonProperty("generator")]
        public string Generator = "superflat";

        [JsonProperty("joinMessage")]
        public string JoinMessage = "§e{0} joined the game";

        [JsonProperty("leaveMessage")]
        public string LeaveMessage = "§e{0} left the game";

        [JsonProperty("onlineMode")]
        public bool OnlineMode = true;

        [JsonProperty("operatorRequests")]
        public bool AllowOperatorRequests = false;

        [JsonProperty("logLevel")]
#if DEBUG
        public LogLevel LogLevel = LogLevel.Debug;

#else
        public LogLevel LogLevel = LogLevel.Error;
#endif

        [JsonProperty("debugMode")]
        public bool DebugMode = false;

        /// <summary>
        /// If true, each login/client gets a random username where multiple connections from the same host will be allowed.
        /// </summary>
        [JsonProperty("multiplayerDebugMode")]
        public bool MulitplayerDebugMode = false;

        [JsonProperty("header")]
        public string Header = "§dObsidian > All other servers";

        [JsonProperty("footer")]
        public string Footer = "§5tiddies §l§d( §c. §d)( §c. §d)";

        [JsonProperty("baah", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Baah = null;
    }
}