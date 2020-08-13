using Newtonsoft.Json;

namespace Obsidian.Util
{
    public class Config
    {
        [JsonProperty("motd")]
        public string Motd = "§dObsidian §rv§c0.1§a-DEVEL\n§r§lRunning on .NET Core 2.1 <3";

        [JsonProperty("port")]
        public int Port = 25565;

        [JsonProperty("generator")]
        public string Generator = "superflat";

        [JsonProperty("joinMessage")]
        public string JoinMessage = "§e{0} joined the game";

        [JsonProperty("leaveMessage")]
        public string LeaveMessage = "§e{0} left the game";

        [JsonProperty("onlineMode")]
        public bool OnlineMode = true;

        [JsonProperty("operatorRequests")]
        public bool AllowOperatorRequests;

        /// <summary>
        /// If true, each login/client gets a random username where multiple connections from the same host will be allowed.
        /// </summary>
        [JsonProperty("multiplayerDebugMode")]
        public bool MulitplayerDebugMode;

        [JsonProperty("header")]
        public string Header = "§dObsidian > All other servers";

        [JsonProperty("footer")]
        public string Footer = "§5tiddies §l§d( §c. §d)( §c. §d)";

        [JsonProperty("baah", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Baah;

        [JsonProperty("maxMissedKeepAlives")]
        public int MaxMissedKeepalives = 15;
    }
}