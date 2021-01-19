using Newtonsoft.Json;
using Obsidian.API;
using System.Collections.Generic;

namespace Obsidian.Chat
{
    public class ChatMessage : IChatMessage
    {
        [JsonProperty("text", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Text { get; set; }

        [JsonProperty("color", DefaultValueHandling = DefaultValueHandling.Ignore)]
        private string HexColor => Color.ToString();
        [JsonIgnore]
        public HexColor Color { get; set; }

        [JsonProperty("bold", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Bold { get; set; }

        [JsonProperty("italic", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Italic { get; set; }

        [JsonProperty("underlined", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Underline { get; set; }

        [JsonProperty("strikethrough", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Strikethrough { get; set; }

        [JsonProperty("obfuscated", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Obfuscated { get; set; }

        [JsonProperty("insertion", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Insertion { get; set; }

        [JsonProperty("clickEvent", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public IClickComponent ClickEvent { get; set; }

        [JsonProperty("hoverEvent", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public IHoverComponent HoverEvent { get; set; }

        [JsonProperty("extra", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<ChatMessage> Extra { get; set; }

        [JsonIgnore]
        public IEnumerable<IChatMessage> Extras => GetExtras();
        public IEnumerable<IChatMessage> GetExtras()
        {
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
        public override string ToString() => JsonConvert.SerializeObject(this, Formatting.Indented, Globals.JsonSettings);

        public string ToString(bool indented) => JsonConvert.SerializeObject(this, indented ? Formatting.Indented : Formatting.None, Globals.JsonSettings);
    }
}