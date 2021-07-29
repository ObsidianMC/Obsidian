using System;
using System.Collections.Generic;

namespace Obsidian.API
{
    public interface IChatMessage
    {
#nullable disable
        internal static Func<IChatMessage> createNew;
#nullable restore
        public static IChatMessage CreateNew() => createNew();

        public static IChatMessage Empty => Simple(string.Empty);

        public string? Text { get; set; }
        public HexColor Color { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underlined { get; set; }
        public bool Strikethrough { get; set; }
        public bool Obfuscated { get; set; }
        public string Insertion { get; set; }
        public IClickComponent? ClickEvent { get; set; }
        public IHoverComponent? HoverEvent { get; set; }
        public IEnumerable<IChatMessage> Extras { get; }

        public IChatMessage AddExtra(IChatMessage chatMessage);
        public IChatMessage AddExtra(IEnumerable<IChatMessage> chatMessages);

        public static IChatMessage Simple(string? text, ChatColor color) => Simple($"{color}{text}");

        public static IChatMessage Simple(string? text)
        {
            IChatMessage chatMessage = CreateNew();
            chatMessage.Text = text;
            return chatMessage;
        }
        public static IChatMessage Simple(string? text, HexColor color)
        {
            IChatMessage chatMessage = CreateNew();
            chatMessage.Text = text;
            chatMessage.Color = color;
            return chatMessage;
        }

        public IChatMessage WithClickAction(EClickAction action, string value, string? translate = null)
        {
            ClickEvent = IClickComponent.CreateNew(action, value, translate);
            return this;
        }

        public IChatMessage WithHoverAction(EHoverAction action, object value, string? translate = null)
        {
            HoverEvent = IHoverComponent.CreateNew(action, value, translate);
            return this;
        }

        public IChatMessage AppendText(string? text)
        {
            if (Text is null)
            {
                Text = text;
            }
            else
            {
                Text += text;
            }
            return this;
        }

        public IChatMessage AppendText(string? text, ChatColor color)
        {
            if (Text is null)
            {
                Text = $"{color}{text}";
            }
            else
            {
                Text += $"{color}{text}";
            }
            return this;
        }
    }
}
