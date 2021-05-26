﻿using Newtonsoft.Json;
using Obsidian.Net;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Utilities.Mojang
{
    public class MojangUser
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("legacy")]
        public bool Legacy { get; set; }

        [JsonProperty("demo")]
        public bool Demo { get; set; }

        [JsonProperty("properties")]
        public List<SkinProperties> Properties { get; set; }
    }

    public class SkinProperties
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("signature")]
        public string Signature { get; set; }

        public async Task<byte[]> ToArrayAsync()
        {
            var isSigned = this.Signature != null;
            using var stream = new MinecraftStream();
            await stream.WriteStringAsync(this.Name, 32767);
            await stream.WriteStringAsync(this.Value, 32767);
            await stream.WriteBooleanAsync(isSigned);
            if (isSigned)
                await stream.WriteStringAsync(this.Signature, 32767);

            return stream.ToArray();
        }

        public byte[] ToArray()
        {
            using var stream = new MinecraftStream();
            stream.WriteString(Name);
            stream.WriteString(Value);
            stream.WriteBoolean(Signature is not null);
            if (Signature is not null)
                stream.WriteString(Signature);

            return stream.ToArray();
        }
    }
}
