﻿using Newtonsoft.Json;
using System.Collections.Generic;

namespace Obsidian.Utilities.Mojang
{
    public class JoinedResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string PlayerName { get; set; }

        [JsonProperty("properties")]
        public List<JoinedProperty> Properties { get; set; }
    }

    public class JoinedProperty
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("signature")]
        public string Signature { get; set; }
    }
}
