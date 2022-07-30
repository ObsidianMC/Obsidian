namespace Obsidian.API;

public interface ICommandSender
{
    public CommandIssuers Issuer { get; }
    public IPlayer Player { get; }
    public Task SendMessageAsync(ChatMessage message, Guid? sender = null);
}
