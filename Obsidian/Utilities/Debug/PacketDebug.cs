using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Utilities.Debug;

public static class PacketDebug
{
    internal static ILogger Logger { get; set; }

    public static Task AppendAsync(string description, byte[] newBytes)
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

        Logger.LogDebug(builder.ToString());

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
}
