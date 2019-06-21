using Obsidian.Chat;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class PlayerListHeaderFooter : Packet
    {
        public PlayerListHeaderFooter(ChatMessage header, ChatMessage footer) : base(0x4E)
        {
            this.Header = header ?? new ChatMessage()
            {
                Components = new List<TextComponent>()
                {
                    new TextComponent()
                    {
                        Translate = ""
                    }
                }
            };

            this.Footer = footer ?? new ChatMessage()
            {
                Components = new List<TextComponent>()
                {
                    new TextComponent()
                    {
                        Translate = ""
                    }
                }
            };
        }

        public ChatMessage Header { get; }

        public ChatMessage Footer { get; }

        public override Task PopulateAsync() => throw new NotImplementedException();

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteChatAsync(Header);
                await stream.WriteChatAsync(Footer);

                return stream.ToArray();
            }
        }
    }
}