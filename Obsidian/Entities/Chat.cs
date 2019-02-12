using Newtonsoft.Json;
using System.Collections.Generic;

namespace Obsidian.Entities
{
    public class Chat
    {
        ///<summary>Makes a new Chat object with plain text (therefore the name "Simple")</summary>
        public static Chat Simple(string text) => new Chat() {Text=text};

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


        [JsonProperty("extra", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Chat> Extra;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}