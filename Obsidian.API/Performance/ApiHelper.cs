using System;
using System.Threading.Tasks;

namespace Obsidian.API.Performance
{
    public static class ApiHelper
    {
        public static Task SendMessageAsync(this IPlayer player, Utf8Message message, MessageType messageType = MessageType.Chat, Guid? sender = null)
        {
            return player.SendMessageAsync(message, messageType, sender);
        }

        public static Task BroadcastAsync(this IServer server, Utf8Message message, MessageType messageType = MessageType.Chat)
        {
            return server.BroadcastAsync(message, messageType);
        }

        public static Utf8Message ToUtf8Message(this ChatMessage chatMessage)
        {
            // TODO: somehow get JSON options (and converters!)
            return new Utf8Message(chatMessage.ToString(null!));
        }
    }
}
