using Microsoft.Extensions.Logging;
using System.Text;

namespace Obsidian.Console;

/// <summary>
/// The console as a command sender: replies are written to the log as plain text, with '§' formatting codes removed.
/// </summary>
internal sealed class ConsoleCommandSender(ILogger logger) : ICommandSender
{
    public CommandIssuers Issuer => CommandIssuers.Console;

    public IPlayer? Player => null;

    public Task SendMessageAsync(ChatMessage message)
    {
        logger.LogInformation("{Message}", ToPlainText(message));

        return Task.CompletedTask;
    }

    public Task SendMessageAsync(ChatMessage message, Guid sender) => this.SendMessageAsync(message);

    /// <summary>The name commands show for this sender, e.g. in /echo.</summary>
    public override string ToString() => "Console";

    private static string ToPlainText(ChatMessage message)
    {
        var builder = new StringBuilder();

        AppendPlainText(builder, message);

        return builder.ToString().Trim('\n');
    }

    private static void AppendPlainText(StringBuilder builder, ChatMessage message)
    {
        AppendPlainText(builder, message.Text);

        foreach (var extra in message.GetExtras())
            AppendPlainText(builder, extra);
    }

    private static void AppendPlainText(StringBuilder builder, string? text)
    {
        if (text is null)
            return;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '§')
                i++;
            else
                builder.Append(text[i]);
        }
    }
}
