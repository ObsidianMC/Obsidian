using Newtonsoft.Json;
using Obsidian.Logging;

namespace Obsidian.Entities
{
    public class GlobalConfig
    {
        [JsonProperty("serverCount")]
        public int ServerCount = 1;

        [JsonProperty("logLevel")]
#if DEBUG
        public LogLevel LogLevel = LogLevel.Debug;

#else
        public LogLevel LogLevel = LogLevel.Error;
#endif

        [JsonProperty("debugMode")]
        public bool DebugMode;
    }
}