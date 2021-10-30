using Obsidian.API.Plugins.Services.Common;
using System;
using System.Diagnostics;

namespace Obsidian.API.Plugins.Services
{
    /// <summary>
    /// Represents a service used to perform logging.
    /// </summary>
    public interface ILogger : IService
    {
        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        public void Log(string message);

        /// <summary>
        /// Formats and writes a debug log message.
        /// </summary>
        /// <param name="message"></param>
        public void LogDebug(string message);

        /// <summary>
        /// Formats and writes a warning log message.
        /// </summary>
        public void LogWarning(string message);

        /// <summary>
        /// Formats and writes an error log message.
        /// </summary>
        public void LogError(string message);

        /// <summary>
        /// Formats and writes an error log message.
        /// </summary>
        public void LogError<T>(T exception) where T : Exception
        {
            LogError($"{nameof(T)}: {exception.Message}");
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        public void LogTrace(string message)
        {
            Log(message);
            Log(new StackTrace(skipFrames: 1).ToString());
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        public void LogTrace<T>(T exception) where T : Exception
        {
            Log($"{nameof(T)}: {exception.Message}");
            if (exception.StackTrace != null)
            {
                Log(exception.StackTrace);
            }
            else
            {
                Log("No stack trace...");
            }
        }
    }
}
