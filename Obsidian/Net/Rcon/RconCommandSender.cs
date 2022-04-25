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

    public Task SendMessageAsync(ChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null)
    {
        builder.Append(message.Text);
        foreach (var extra in message.GetExtras()) builder.Append(extra.Text);

        return Task.CompletedTask;
    }

    public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null) => SendMessageAsync(ChatMessage.Simple(message), type, sender);

    public string GetResponse() => builder.ToString();
}
