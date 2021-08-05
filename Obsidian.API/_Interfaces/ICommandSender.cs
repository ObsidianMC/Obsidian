using System;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface ICommandSender
    {
        public CommandIssuers Issuer { get; }
        public IPlayer Player { get; }
        public Task SendMessageAsync(ChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null);
        public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null);
    }
}
