using System.Text;

namespace Obsidian.Net.Rcon;

public class RconCommandSender : ICommandSender
{
    public IPlayer Player => null!;
    public CommandIssuers Issuer => CommandIssuers.RemoteConsole;

    private readonly StringBuilder builder;

    public RconCommandSender()
    {
        builder = new StringBuilder();
    }

    public Task SendMessageAsync(ChatMessage message, Guid? sender = null)
    {
        builder.Append(message.Text);
        foreach (var extra in message.GetExtras()) builder.Append(extra.Text);

        return Task.CompletedTask;
    }

    public string GetResponse() => builder.ToString();
}
