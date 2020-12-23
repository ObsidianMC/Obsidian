using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Obsidian.API;
using Obsidian.Util;
using Obsidian.Util.Converters;
using Obsidian.Util.Registry.Enums;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace Obsidian
{
    public static class Globals
    {
        public static HttpClient HttpClient { get; } = new HttpClient();
        public static Random Random { get; } = new Random();
        public static GlobalConfig Config { get; set; }
        public static ILogger PacketLogger { get; set; }
        public static DefaultContractResolver ContractResolver { get; } = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        };

        public static JsonSerializerSettings JsonSettings { get; } = new JsonSerializerSettings
        {
            ContractResolver = ContractResolver,
            Converters = new List<JsonConverter>
            {
                new DefaultEnumConverter<CustomDirection>(),
                new DefaultEnumConverter<Axis>(),
                new DefaultEnumConverter<Face>(),
                new DefaultEnumConverter<BlockFace>(),
                new DefaultEnumConverter<EHalf>(),
                new DefaultEnumConverter<Hinge>(),
                new DefaultEnumConverter<Instruments>(),
                new DefaultEnumConverter<Part>(),
                new DefaultEnumConverter<Shape>(),
                new DefaultEnumConverter<CustomDirection>(),
                new DefaultEnumConverter<MinecraftType>(),
                new DefaultEnumConverter<Attachment>(),
                new DefaultEnumConverter<ETextAction>()
            }
        };
    }
}
