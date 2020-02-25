using System.Diagnostics;
using System.Linq;
using System.Text;
using Obsidian.Logging;

namespace Obsidian.Util.Debug
{
    public static class PacketDebug
    {
        private static AsyncLogger Logger { get; } = new AsyncLogger("Packet Debugger", LogLevel.Debug, "packet-debug.log");

        public static void Append(string description, byte[] newBytes)
        {
            var builder = new StringBuilder();
            builder.AppendLine("====================");
            
            builder.AppendLine("INFO ---------------");
            builder.AppendLine(description);
            
            builder.AppendLine("DATA ---------------");
            builder.AppendLine(ToString(newBytes));
            
            builder.AppendLine("STACK --------------");
            builder.AppendLine(GetStack());
            
            builder.AppendLine("====================");

            Logger.LogDebugAsync(builder.ToString());
        }

        private static string GetStack()
        {
            var stack = new StackTrace();

            return string.Join("\n",
                               stack.ToString()
                                    .Split('\n')
                                    .Skip(2)
                                    .Where((line) => !line.StartsWith("   at System.")));
        }

        private static string ToString(byte[] bytes)
        {
            var builder = new StringBuilder();

            foreach (var @byte in bytes)
            {
                builder.Append(@byte.ToString("X2") + " ");
            }

            return builder.ToString();
        }
    }
}