﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Obsidian.API;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Obsidian.Utilities
{
    public class ServerStatus : IServerStatus
    {
        private const string b64obsidian = "data:image/png;base64,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";

        [JsonProperty("version")]
        public IServerVersion Version { get; set; }

        [JsonProperty("players")]
        public IServerPlayers Players { get; set; }

        [JsonProperty("description")]
        public IServerDescription Description { get; set; }

        /// <summary>
        /// This is a base64 png image, that has dimensions of 64x64.
        /// </summary>
        [JsonProperty("favicon")]
        public string Favicon { get; set; }

        /// <summary>
        /// Generates a server status from the specified <paramref name="server"/>.
        /// </summary>
        public ServerStatus(Server server)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));


            this.Version = new ServerVersion();
            this.Players = new ServerPlayers(server);
            this.Description = (IServerDescription)new ServerDescription(server);
            var favicon_file = "favicon.png";
            if (File.Exists(favicon_file))
            {
                byte[] imageArray = File.ReadAllBytes(@"favicon.png");
                byte[] valid_PNG_Header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                var isValidImage = !valid_PNG_Header.Select((x, i) => x.Equals(imageArray[i])).Contains(false);
                if (isValidImage)
                {
                    string b64 = Convert.ToBase64String(imageArray);
                    this.Favicon = $"data:image/png;base64,{b64}";
                }
                else
                {
                    server.Logger.LogError("The favicon.png is invalid! Skipping it.");
                    this.Favicon = b64obsidian;
                }
            }
            else
            {
                this.Favicon = b64obsidian; //TOOD: add proper image
            }
        }
    }

    public class ServerVersion : IServerVersion
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("protocol")]
        public ProtocolVersion Protocol { get; set; }

        public ServerVersion(string name = null, ProtocolVersion? protocol = null)
        {
            this.Name = name ?? $"Obsidian {(protocol ?? Server.protocol).GetDescription()}";
            this.Protocol = protocol ?? Server.protocol;
        }
    }

    public class ServerPlayers : IServerPlayers
    {
        [JsonProperty("max")]
        public int Max { get; set; }

        [JsonProperty("online")]
        public int Online { get; set; }

        [JsonProperty("sample")]
        public List<object> Sample { get; set; } = new List<object>();

        public ServerPlayers(Server server)
        {
            var players = server.OnlinePlayers.Values;

            Max = server.Config.MaxPlayers;
            Online = players.Count();

            if (this.Online > 0)
            {
                foreach (var player in players)
                {
                    this.Sample.Add(new
                    {
                        name = player.Username,
                        id = player.Uuid
                    });
                }
            }
        }

        public void Clear()
        {
            this.Sample.Clear();
        }

        public void AddPlayer(string username, Guid uuid)
        {
            this.Sample.Add(new
            {
                name = username,
                id = uuid
            });
        }
    }

    public class ServerDescription : IServerDescription
    {
        [JsonIgnore]
        public string Text { get { return text; } set { text = value.Replace('&', '§'); } }

        [JsonProperty("text")]
        private string text;

        public ServerDescription(Server server) => this.Text = server.Config.Motd;
    }
}