using Microsoft.Extensions.Logging;
using Obsidian.API;
using System;
using System.Threading.Tasks;

namespace Obsidian.Commands.Framework.Entities
{
    public class CommandSender : ICommandSender
    {
        public IPlayer Player { get; }
        public CommandIssuers Issuer { get; }
        public ILogger Logger { get; }

        public CommandSender(CommandIssuers issuer, IPlayer player, ILogger logger)
        {
            Issuer = issuer;
            Player = player;
            Logger = logger;
        }

        public async Task SendMessageAsync(ChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null)
        {
            if (Issuer == CommandIssuers.Client)
            {
                await Player.SendMessageAsync(message, type, sender);
                return;
            }

            string messageString = message.Text;
            foreach (var extra in message.Extras)
            {
                messageString += extra.Text;
            }

            Logger.LogInformation(messageString);
        }

        public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null) => SendMessageAsync(ChatMessage.Simple(message), type, sender);
    }
}
