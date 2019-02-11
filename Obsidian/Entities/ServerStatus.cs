using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.Entities
{
    public class ServerStatus
    {
        private const string b64obsidian = "data:image/png;base64,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";

        [JsonProperty("version")]
        public ServerVersion Version;

        [JsonProperty("players")]
        public ServerPlayers Players;

        [JsonProperty("description")]
        public ServerDescription Description;

        /// <summary>
        /// This is a base64 png image, that has dimensions of 64x64
        /// </summary>
        [JsonProperty("favicon")]
        public string Favicon;

        public static ServerStatus DebugStatus => new ServerStatus()
        {
            Description = new ServerDescription()
            {
                Text = "§dObsidian §rv§c0.1§a-DEVEL\n§r§lRunning on .NET Core 2.1 <3"
            },
            Favicon = "",
            Players = new ServerPlayers()
            {
                Max = 696969,
                Online = 420420,
                Sample = new object[0]
            },
            Version = new ServerVersion()
            {
                Name = "Obsidian 1.13.2",
                Protocol = ProtocolVersion.v1_13_2
            }
        };

    }

    public class ServerVersion
    {
        [JsonProperty("name")]
        public string Name;

        [JsonProperty("protocol")]
        public ProtocolVersion Protocol;
    }

    public class ServerPlayers
    {
        [JsonProperty("max")]
        public int Max;

        [JsonProperty("online")]
        public int Online;

        [JsonProperty("sample")]
        public object[] Sample;//I think sample of online users, TODO
    }

    public class ServerDescription
    {
        [JsonProperty("text")]
        public string Text;
    }

}
