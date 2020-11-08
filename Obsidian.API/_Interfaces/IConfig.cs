namespace Obsidian.API
{
    public interface IConfig
    {
        public string Motd { get; set; }

        public int Port { get; set; }

        public string Generator { get; set; }

        public string Seed { get; set; }

        public string JoinMessage { get; set; }

        public string LeaveMessage { get; set; }

        public bool OnlineMode { get; set; }

        public int MaxPlayers { get; set; }

        public bool AllowOperatorRequests { get; set; }

        /// <summary>
        /// Whether each login/client gets a random username where multiple connections from the same host will be allowed.
        /// </summary>
        public bool MulitplayerDebugMode { get; set; }

        public string Header { get; set; }

        public string Footer { get; set; }

        public int MaxMissedKeepAlives { get; set; }

        public string[] DownloadPlugins { get; set; }
    }
}
