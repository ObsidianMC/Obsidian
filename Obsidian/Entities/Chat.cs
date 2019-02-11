using Newtonsoft.Json;
using System.Collections.Generic;

namespace Obsidian.Entities
{
    public class Chat
    {
        [JsonProperty("text")]
        public string Text;

        [JsonProperty("bold")]
        public bool Bold;

        [JsonProperty("extra", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Chat> Extra;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}