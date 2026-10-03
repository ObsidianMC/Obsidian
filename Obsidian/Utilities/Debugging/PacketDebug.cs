using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace Obsidian.Utilities.Debugging;

public static partial class PacketDebug
{
    internal static ILogger Logger { get; set; }

    public static Task AppendAsync(string description, byte[] newBytes)
    {
        if (!Logger.IsEnabled(LogLevel.Debug))
            return Task.CompletedTask;

        var builder = new StringBuilder();
        builder.AppendLine("====================");

        builder.AppendLine("INFO ---------------");
        builder.AppendLine(description);

        builder.AppendLine("DATA ---------------");
        builder.AppendLine(ToString(newBytes));

        builder.AppendLine("STACK --------------");
        builder.AppendLine(GetStack());

        builder.AppendLine("====================");

        var dump = builder.ToString();
        Log.PacketDump(Logger, dump);

        return Task.CompletedTask;
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

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "{PacketDump}")]
        public static partial void PacketDump(ILogger logger, string packetDump);
    }
}
