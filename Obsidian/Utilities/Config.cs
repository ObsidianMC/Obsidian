﻿using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Obsidian.API;
using System;

namespace Obsidian.Utilities
{
    public class Config : IConfig
    {
        public string Motd { get; set; } = "§k||||§r §5Obsidian §cPre§r-§cRelease §r§k||||§r \n§r§lRunning on .NET §l§c5 §r§l<3";

        public int Port { get; set; } = 25565;

        public string Generator { get; set; } = "overworld";

        public string Seed { get; set; } = "Obsidian691337";

        public string JoinMessage { get; set; } = "§e{0} joined the game";

        public string LeaveMessage { get; set; } = "§e{0} left the game";

        public bool OnlineMode { get; set; } = false;
        public int MaxPlayers { get; set; } = 1000000;

        public bool AllowOperatorRequests { get; set; }

        /// <summary>
        /// If true, each login/client gets a random username where multiple connections from the same host will be allowed.
        /// </summary>
        public bool MulitplayerDebugMode { get; set; }

        public string Header { get; set; } = "§dObsidian > All other servers";

        public string Footer { get; set; } = "§5tiddies §l§d( §c. §d)( §c. §d)";

        public bool? Baah { get; set; }

        public int MaxMissedKeepAlives { get; set; } = 15;

        public string[] DownloadPlugins { get; set; } = Array.Empty<string>();

        public bool UDPBroadcast = false;
        
        public int PregenerateChunkRange { get; set; } = 15; // by default, pregenerate range from -15 to 15

        public LogLevel LogLevel { get; set; } = LogLevel.Information;

        public bool VerboseExceptionLogging => LogLevel is LogLevel.Debug or LogLevel.Trace;
    }
}