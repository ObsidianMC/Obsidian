using Microsoft.Extensions.Logging;
using System;

namespace Obsidian.Plugins.Services
{
    public class LoggerService : Logging.Logger, API.Plugins.Services.ILogger
    {
        public LoggerService(PluginContainer plugin) : base(plugin.Info.Name, Globals.Config.LogLevel)
        {
        }

        internal LoggerService(string prefix, LogLevel minLevel = LogLevel.Debug) : base(prefix, minLevel)
        {
        }

        public void Log(string message)
        {
            Log(LogLevel.Information, default, message, default, Format);
        }

        public void LogDebug(string message)
        {
            Log(LogLevel.Debug, default, message, default, Format);
        }

        public void LogError(string message)
        {
            Log(LogLevel.Error, default, message, default, Format);
        }

        public void LogWarning(string message)
        {
            Log(LogLevel.Warning, default, message, default, Format);
        }

        private static string Format(string message, Exception exception)
        {
            if (exception is null)
                return message;
            
            return message + exception.ToString();
        }
    }
}
