using Obsidian.API;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Chat
{
    public class ChatMessage : IChatMessage
    {
        public string Text { get; set; }

        [JsonPropertyName("color")]
        private string HexColor => Color.ToString();

        [JsonIgnore]
        public HexColor Color { get; set; }

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        public bool Underlined { get; set; }

        public bool Strikethrough { get; set; }

        public bool Obfuscated { get; set; }

        public string Insertion { get; set; }

        public IClickComponent ClickEvent { get; set; }

        public IHoverComponent HoverEvent { get; set; }

        public List<ChatMessage> Extra { get; set; }

        [JsonIgnore]
        public IEnumerable<IChatMessage> Extras => GetExtras();

        public IEnumerable<IChatMessage> GetExtras()
        {
            if (Extra == null)
                yield break;

            foreach (var extra in Extra)
            {
                yield return extra;
            }
        }

        /// <summary>
        /// Creates a new <see cref="ChatMessage"/> object with plain text.
        /// </summary>
        public static ChatMessage Simple(string text) => new ChatMessage() { Text = text.Replace('&', '§') };

        public ChatMessage AddExtra(ChatMessage message)
        {
            Extra ??= new List<ChatMessage>();
            Extra.Add(message);

            return this;
        }

        public ChatMessage AddExtra(List<ChatMessage> messages)
        {
            Extra ??= new List<ChatMessage>(capacity: messages.Count);
            Extra.AddRange(messages);

            return this;
        }

        public IChatMessage AddExtra(IChatMessage message)
        {
            return AddExtra(message as ChatMessage);
        }

        public IChatMessage AddExtra(IEnumerable<IChatMessage> messages)
        {
            foreach (var message in messages)
            {
                AddExtra(message);
            }

            return this;
        }

        public static ChatMessage Empty => Simple(string.Empty);

        public static implicit operator ChatMessage(string text) => Simple(text);

        public override string ToString() => JsonSerializer.Serialize(this, Globals.JsonOptions);
    }
}