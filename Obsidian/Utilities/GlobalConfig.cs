using Microsoft.Extensions.Logging;

namespace Obsidian.Utilities
{
    public class GlobalConfig
    {
        public int ServerCount = 1;

#if DEBUG
        public LogLevel LogLevel = LogLevel.Debug;

#else
        public LogLevel LogLevel = LogLevel.Information;
#endif

        public bool DebugMode;

        public bool VerboseExceptionLogging { get; set; } = false;
    }
}