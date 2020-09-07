using Newtonsoft.Json;
using Obsidian.Util.Converters;
using System.Collections.Generic;

namespace Obsidian.Chat
{
    public class ChatMessage
    {
        ///<summary>Makes a new Chat object with plain text (therefore the name "Simple")</summary>
        public static ChatMessage Simple(string text) => new ChatMessage() {Text=text};

        [JsonProperty("text", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Text;

        [JsonProperty("bold", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Bold;

        [JsonProperty("italic", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Italic;

        [JsonProperty("underlined", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Underline;

        [JsonProperty("strikethrough", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Strikethrough;

        [JsonProperty("obfuscated", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Obfuscated;

        [JsonProperty("insertion", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Insertion;

        [JsonProperty("clickEvent", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public TextComponent ClickEvent;

        [JsonProperty("hoverEvent", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public TextComponent HoverEvent;

        //[JsonProperty("with", DefaultValueHandling = DefaultValueHandling.Ignore)]
        //public List<TextComponent> Components;

        [JsonProperty("extra", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<ChatMessage> Extra;

        /*public ChatMessage AddComponent(TextComponent component)
        {
            if (this.Components == null)
                this.Components = new List<TextComponent>();

            this.Components.Add(component);

            return this;
        }*/

        public ChatMessage AddExtra(ChatMessage message)
        {
            if (this.Extra == null)
                this.Extra = new List<ChatMessage>();

            this.Extra.Add(message);

            return this;
        }

        public ChatMessage AddExtra(List<ChatMessage> messages)
        {
            if (this.Extra == null)
                this.Extra = new List<ChatMessage>();

            this.Extra.AddRange(messages);

            return this;
        }


        public override string ToString() => JsonConvert.SerializeObject(this, Program.JsonSettings);
    }
}