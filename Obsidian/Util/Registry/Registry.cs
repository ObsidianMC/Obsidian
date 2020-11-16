using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Util.Extensions;
using Obsidian.Util.Registry.Codecs;
using Obsidian.Util.Registry.Codecs.Biomes;
using Obsidian.Util.Registry.Codecs.Dimensions;
using Obsidian.Util.Registry.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Util.Registry
{
    public class Registry
    {
        internal static ILogger Logger { get; set; }

        public static Dictionary<Materials, Item> Items = new Dictionary<Materials, Item>();
        public static readonly string[] Blocks = new string[763];

        public static Dictionary<string, List<Tag>> Tags = new Dictionary<string, List<Tag>>();

        public static readonly MatchTarget[] StateToMatch = new MatchTarget[17112];
        public static readonly short[] NumericToBase = new short[763];

        internal static CodecCollection<int, DimensionCodec> DefaultDimensions { get; } = new CodecCollection<int, DimensionCodec>("minecraft:dimension_type");

        internal static CodecCollection<string, BiomeCodec> DefaultBiomes { get; } = new CodecCollection<string, BiomeCodec>("minecraft:worldgen/biome");

        private static Dictionary<string, List<string>> resources = new Dictionary<string, List<string>>();

        private readonly static string mainDomain = "Obsidian.Assets";

        static Registry()
        {
            var res = Assembly.GetExecutingAssembly().GetManifestResourceNames();

            foreach (var resource in res)
            {
                if (!resource.StartsWith("Obsidian.Assets.Tags."))
                {
                    if (resources.ContainsKey("assets"))
                        resources["assets"].Add(resource);
                    else
                        resources["assets"] = new List<string>
                        {
                            resource
                        };
                }
                else
                {
                    var newName = resource.Replace("Obsidian.Assets.Tags.", "");

                    if (newName.StartsWith("blocks."))
                    {
                        newName = resource.Replace("blocks.", "");

                        if (resources.ContainsKey("blocks"))
                            resources["blocks"].Add(resource);
                        else
                            resources["blocks"] = new List<string> { resource };
                    }
                    else if (newName.StartsWith("items."))
                    {
                        if (resources.ContainsKey("items"))
                            resources["items"].Add(resource);
                        else
                            resources["items"] = new List<string> { resource };
                    }
                    else if (newName.StartsWith("fluids."))
                    {
                        if (resources.ContainsKey("fluids"))
                            resources["fluids"].Add(resource);
                        else
                            resources["fluids"] = new List<string> { resource };
                    }
                    else if (newName.StartsWith("entity_types."))
                    {
                        if (resources.ContainsKey("entity_types"))
                            resources["entity_types"].Add(resource);
                        else
                            resources["entity_types"] = new List<string> { resource };
                    }
                }
            }
        }

        public static async Task RegisterBlocksAsync()
        {
            var blocks = resources["assets"].Where(x => x.EqualsIgnoreCase($"{mainDomain}.blocks.json")).First();

            var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream(blocks);

            using var read = new StreamReader(fs, new UTF8Encoding(false));

            string json = await read.ReadToEndAsync();

            int registered = 0;

            var type = JObject.Parse(json);

            using (var enumerator = type.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    var (blockName, token) = enumerator.Current;

                    var name = blockName.Split(":")[1];

                    var states = JsonConvert.DeserializeObject<BlockJson>(token.ToString(), Globals.JsonSettings);

                    if (!Enum.TryParse(name.Replace("_", ""), true, out Materials material))
                        continue;

                    if (states.States.Length <= 0)
                        continue;

                    int id = 0;
                    foreach (var state in states.States)
                        id = state.Default ? state.Id : states.States.First().Id;

                    var baseId = (short)states.States.Min(state => state.Id);
                    NumericToBase[(int)material] = baseId;

                    Blocks[(int)material] = "minecraft:" + name;

                    foreach (var state in states.States)
                    {
                        StateToMatch[state.Id] = new MatchTarget(baseId, (short)material);

                        if (id == state.Id)
                            continue;
                    }
                    registered++;
                }
            }

            Logger.LogDebug($"Successfully registered {registered} blocks..");
        }

        public static async Task RegisterItemsAsync()
        {
            var items = resources["assets"].Where(x => x.EqualsIgnoreCase($"{mainDomain}.items.json")).First();

            var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream(items);

            using var read = new StreamReader(fs, new UTF8Encoding(false));

            var json = await read.ReadToEndAsync();

            var type = JObject.Parse(json);

            using var enumerator = type.GetEnumerator();
            int registered = 0;

            while (enumerator.MoveNext())
            {
                var (name, token) = enumerator.Current;

                var itemName = name.Split(":")[1];

                var item = JsonConvert.DeserializeObject<BaseRegistryJson>(token.ToString());

                if (!Enum.TryParse(itemName.Replace("_", ""), true, out Materials material))
                    continue;

                Items.Add(material, new Item(material) { Id = item.ProtocolId, UnlocalizedName = name });
                registered++;
            }

            Logger.LogDebug($"Successfully registered {registered} items..");
        }

        public static async Task RegisterBiomesAsync()
        {
            var dimensions = resources["assets"].Where(x => x.EqualsIgnoreCase($"{mainDomain}.biome_dimension_codec.json")).First();

            using var cfs = Assembly.GetExecutingAssembly().GetManifestResourceStream(dimensions);

            using var cread = new StreamReader(cfs, new UTF8Encoding(false));

            var json = await cread.ReadToEndAsync();

            var type = JObject.Parse(json);

            using var cenumerator = type.GetEnumerator();

            var registered = 0;
            while (cenumerator.MoveNext())
            {
                var (name, token) = cenumerator.Current;

                foreach (var obj in token)
                {
                    var val = obj.ToString();
                    var codec = JsonConvert.DeserializeObject<BiomeCodec>(val, Globals.JsonSettings);

                    DefaultBiomes.TryAdd(codec.Name, codec);

                    registered++;
                }
            }
            Logger.LogDebug($"Successfully registered {registered} codec biomes");
        }

        public static async Task RegisterDimensionsAsync()
        {
            var dimensions = resources["assets"].Where(x => x.EqualsIgnoreCase($"{mainDomain}.default_dimensions.json")).First();

            using var cfs = Assembly.GetExecutingAssembly().GetManifestResourceStream(dimensions);

            using var cread = new StreamReader(cfs, new UTF8Encoding(false));

            var json = await cread.ReadToEndAsync();

            var type = JObject.Parse(json);

            using var cenumerator = type.GetEnumerator();

            var registered = 0;
            while (cenumerator.MoveNext())
            {
                var (name, token) = cenumerator.Current;

                foreach (var obj in token)
                {
                    var val = obj.ToString();
                    var codec = JsonConvert.DeserializeObject<DimensionCodec>(val, Globals.JsonSettings);

                    DefaultDimensions.TryAdd(codec.Id, codec);

                    Logger.LogDebug($"Added codec: {codec.Name}:{codec.Id}");
                    registered++;
                }
            }
            Logger.LogDebug($"Successfully registered {registered} codec dimensions");
        }

        public static async Task RegisterTagsAsync()
        {
            var domains = new Dictionary<string, List<DomainTag>>();
            var registered = 0;


            foreach (var (baseTagName, tags) in resources)
            {
                if (baseTagName.EqualsIgnoreCase("assets"))
                    continue;

                foreach (var tag in tags)
                {
                    using var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream(tag);

                    using var read = new StreamReader(fs, new UTF8Encoding(false));

                    var tagName = tag.Replace($"{mainDomain}.Tags.{baseTagName}.", "").Replace(".json", "");
                    var json = await read.ReadToEndAsync();

                    var type = JObject.Parse(json);

                    using var enumerator = type.GetEnumerator();

                    while (enumerator.MoveNext())
                    {
                        var (name, token) = enumerator.Current;

                        if (name.EqualsIgnoreCase("values"))
                        {
                            var list = token.ToObject<List<string>>();

                            var ids = new List<int>();

                            foreach (var item in list)
                            {
                                if (item.StartsWith("#"))
                                {
                                    var start = item.TrimStart('#');

                                    if (domains.ContainsKey(start))
                                        domains[start].Add(new DomainTag
                                        {
                                            TagName = tagName,
                                            BaseTagName = baseTagName
                                        });
                                    else
                                        domains.Add(start, new List<DomainTag>
                                        {
                                            new DomainTag
                                            {
                                                TagName = tagName,
                                                BaseTagName = baseTagName
                                            }
                                        });

                                    continue;
                                }

                                object obj = null;
                                switch (baseTagName)
                                {
                                    case "blocks":
                                        obj = GetBlock(item);
                                        break;
                                    case "items":
                                        obj = GetItem(item);
                                        break;
                                    default:
                                        if (Enum.TryParse<EntityType>(item.Replace("minecraft:", "").Replace("_", ""), true, out var entityType))
                                            obj = (int)entityType;
                                        else if (Enum.TryParse<Fluids>(item.Replace("minecraft:", "").Replace("_", ""), true, out var fluid))
                                            obj = (int)fluid;
                                        break;
                                }

                                if (obj is SebastiansBlock block)
                                    ids.Add(block.Id);
                                else if (obj is Item returnItem)
                                    ids.Add(returnItem.Id);
                                else if (obj is int value)
                                    ids.Add(value);
                            }

                            if (Tags.ContainsKey(baseTagName))
                            {
                                Tags[baseTagName].Add(new Tag
                                {
                                    Name = tagName,
                                    Entries = ids,
                                    Count = ids.Count
                                });
                            }
                            else
                            {
                                Tags.Add(baseTagName, new List<Tag>
                                {
                                    new Tag
                                    {
                                        Name = tagName,
                                        Entries = ids,
                                        Count = ids.Count
                                    }
                                });
                            }
                        }
                    }
                    registered++;
                }
            }

            if (domains.Count > 0)
            {
                foreach (var (t, domainTags) in domains)
                {
                    var item = t.Replace("minecraft:", "");

                    foreach (var domainTag in domainTags)
                    {
                        var index = Tags[domainTag.BaseTagName].FindIndex(x => x.Name.EqualsIgnoreCase(item));

                        var tag = Tags[domainTag.BaseTagName][index];

                        var tagIndex = Tags[domainTag.BaseTagName].FindIndex(x => x.Name.EqualsIgnoreCase(domainTag.TagName));

                        Tags[domainTag.BaseTagName][tagIndex].Count += tag.Count;

                        Tags[domainTag.BaseTagName][tagIndex].Entries.AddRange(tag.Entries);

                        registered++;
                    }

                }
            }
        }

        public static SebastiansBlock GetBlock(Materials material) => new SebastiansBlock(material);

        public static SebastiansBlock GetBlock(int id) => new SebastiansBlock(id);

        public static SebastiansBlock GetBlock(string unlocalizedName) =>
            new SebastiansBlock(NumericToBase[Array.IndexOf(Blocks, unlocalizedName)]);

        public static Item GetItem(int id) => Items.Values.SingleOrDefault(x => x.Id == id);
        public static Item GetItem(Materials mat) => Items.GetValueOrDefault(mat);
        public static Item GetItem(string unlocalizedName) =>
            Items.Values.SingleOrDefault(x => x.UnlocalizedName.EqualsIgnoreCase(unlocalizedName));

        class BaseRegistryJson
        {
            [JsonProperty("protocol_id")]
            public int ProtocolId { get; set; }
        }

    }

    public class DomainTag
    {
        public string TagName { get; set; }
        public string BaseTagName { get; set; }
    }

    public struct MatchTarget
    {
        public short @base;
        public short numeric;

        public MatchTarget(short @base, short numeric)
        {
            this.@base = @base;
            this.numeric = numeric;
        }
    }
}
