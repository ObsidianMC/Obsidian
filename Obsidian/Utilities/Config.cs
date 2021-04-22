using Newtonsoft.Json;
using Obsidian.API;
using System;

namespace Obsidian.Utilities
{
    public class Config : IConfig
    {
        [JsonProperty("motd")]
        public string Motd { get; set; } = "§k||||§r §5Obsidian §cPre§r-§cRelease §r§k||||§r \n§r§lRunning on .NET §l§c5 §r§l<3";

        [JsonProperty("port")]
        public int Port { get; set; } = 25565;

        [JsonProperty("generator")]
        public string Generator { get; set; } = "overworld";

        [JsonProperty("seed")]
        public string Seed { get; set; } = "Obsidian691337";

        [JsonProperty("joinMessage")]
        public string JoinMessage { get; set; } = "§e{0} joined the game";

        [JsonProperty("leaveMessage")]
        public string LeaveMessage { get; set; } = "§e{0} left the game";

        [JsonProperty("onlineMode")]
        public bool OnlineMode { get; set; } = false;

        [JsonProperty("maxPlayers")]
        public int MaxPlayers { get; set; } = 1000000;

        [JsonProperty("operatorRequests")]
        public bool AllowOperatorRequests { get; set; }

        /// <summary>
        /// If true, each login/client gets a random username where multiple connections from the same host will be allowed.
        /// </summary>
        [JsonProperty("multiplayerDebugMode")]
        public bool MulitplayerDebugMode { get; set; }

        [JsonProperty("header")]
        public string Header { get; set; } = "§dObsidian > All other servers";

        [JsonProperty("footer")]
        public string Footer { get; set; } = "§5tiddies §l§d( §c. §d)( §c. §d)";

        [JsonProperty("baah", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Baah { get; set; }

        [JsonProperty("maxMissedKeepAlives")]
        public int MaxMissedKeepAlives { get; set; } = 15;

        [JsonProperty("downloadplugins")]
        public string[] DownloadPlugins { get; set; } = Array.Empty<string>();

        [JsonProperty("udpBroadcast")]
        public bool UDPBroadcast = false;
        
        [JsonProperty("pregenerateChunkRange")]
        public int PregenerateChunkRange { get; set; } = 15; // by default, pregenerate range from -15 to 15
    }
}