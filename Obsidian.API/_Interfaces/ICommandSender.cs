using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface ICommandSender
    {

        public CommandIssuers Issuer { get; }
        public IPlayer Player { get; }
        public Task SendMessageAsync(IChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null);
        public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null);

    }
}
