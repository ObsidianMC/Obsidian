using Obsidian.Chat;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Client
{
    public class ChatMessagePacket : Packet
    {
        [Field(0)]
        public ChatMessage Message { get; private set; }

        [Field(1)]
        public sbyte Position { get; private set; } // 0 = chatbox, 1 = system message, 2 = game info (actionbar)

        [Field(2, Type = DataType.Array)]
        public List<long> Sender { get; private set; } = new List<long>
        {
            0, 0
        };

        public ChatMessagePacket() : base(0x0E) { }

        public ChatMessagePacket(ChatMessage message, sbyte position, Guid sender) : base(0x0E)
        {
            this.Message = message;
            this.Position = position;
            //this.Sender = sender;
        }
    }
}