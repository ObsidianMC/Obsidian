using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Obsidian.Util
{
    public class GlobalConfig
    {
        [JsonProperty("serverCount")]
        public int ServerCount = 1;

        [JsonProperty("logLevel")]
#if DEBUG
        public LogLevel LogLevel = LogLevel.Debug;

#else
        public LogLevel LogLevel = LogLevel.Information;
#endif

        [JsonProperty("debugMode")]
        public bool DebugMode;

        [JsonProperty("verboseExceptionLogging")]
        public bool VerboseLogging { get; set; } = false;
    }
}