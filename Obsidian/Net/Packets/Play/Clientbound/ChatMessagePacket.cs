using Obsidian.API;
using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class ChatMessagePacket : IClientboundPacket
    {
        [Field(0)]
        public ChatMessage Message { get; private set; }

        [Field(1)]
        public sbyte Position { get; private set; } // 0 = chatbox, 1 = system message, 2 = game info (actionbar)

        [Field(2), FixedLength(2)]
        public List<long> Sender { get; private set; } = new(2)
        {
            0, 0
        };

        public int Id => 0x0E;

        public ChatMessagePacket(ChatMessage message, MessageType type, Guid sender)
        {
            Message = message;
            Position = (sbyte)type;
            //Sender = sender;
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}