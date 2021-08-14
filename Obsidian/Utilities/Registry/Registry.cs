﻿using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Crafting;
using Obsidian.Commands;
using Obsidian.Commands.Parsers;
using Obsidian.Items;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities.Converters;
using Obsidian.Utilities.Registry.Codecs;
using Obsidian.Utilities.Registry.Codecs.Biomes;
using Obsidian.Utilities.Registry.Codecs.Dimensions;
using Obsidian.Utilities.Registry.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Obsidian.Utilities.Registry
{
    public static partial class Registry
    {
        internal static ILogger Logger { get; set; }

        public static DeclareCommands DeclareCommandsPacket = new();

        public static readonly Dictionary<Material, Item> Items = new();
        public static readonly Dictionary<string, IRecipe> Recipes = new();
        public static readonly Dictionary<string, List<Tag>> Tags = new();

        public static readonly MatchTarget[] StateToMatch = new MatchTarget[17_112]; // 17 111 - highest block state
        public static readonly string[] BlockNames = new string[763]; // 762 - block count
        public static readonly short[] NumericToBase = new short[763]; // 762 - highest block numeric id

        public static CodecCollection<int, DimensionCodec> Dimensions { get; } = new("minecraft:dimension_type");
        public static CodecCollection<string, BiomeCodec> Biomes { get; } = new("minecraft:worldgen/biome");

        private static readonly string mainDomain = "Obsidian.Assets";

        private static readonly JsonSerializerOptions blockJsonOptions = new(Globals.JsonOptions)
        {   
            Converters =
            {
                new StringToBoolConverter(),
                new DefaultEnumConverter<CustomDirection>(),
                new DefaultEnumConverter<Axis>(),
                new DefaultEnumConverter<Face>(),
                new DefaultEnumConverter<BlockFace>(),
                new DefaultEnumConverter<EHalf>(),
                new DefaultEnumConverter<Hinge>(),
                new DefaultEnumConverter<Instruments>(),
                new DefaultEnumConverter<Part>(),
                new DefaultEnumConverter<Shape>(),
                new DefaultEnumConverter<MinecraftType>(),
                new DefaultEnumConverter<Attachment>(),
                new DefaultEnumConverter<Mode>(),
            },
        };

        private static readonly JsonSerializerOptions codecJsonOptions = new(Globals.JsonOptions)
        {
            PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance,
        };

        public static async Task RegisterBlocksAsync()
        {
            using Stream fs = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{mainDomain}.blocks.json");

            var dict = await fs.FromJsonAsync<Dictionary<string, BlockJson>>(blockJsonOptions);

            int registered = 0;
            foreach (var (blockName, value) in dict)
            {
                var name = blockName[(blockName.IndexOf(':') + 1)..];

                if (!Enum.TryParse(name.Replace("_", ""), true, out Material material))
                    continue;

                if (value.States.Length <= 0)
                    continue;

                int id = 0;
                foreach (var state in value.States)
                    id = state.Default ? state.Id : value.States.First().Id;

                var baseId = (short)value.States.Min(state => state.Id);
                NumericToBase[(int)material] = baseId;

                BlockNames[(int)material] = blockName;

                foreach (var state in value.States)
                {
                    StateToMatch[state.Id] = new MatchTarget(baseId, (short)material);
                }
                registered++;
            }

            Logger?.LogDebug($"Successfully registered {registered} blocks...");
        }

        public static async Task RegisterItemsAsync()
        {
            using Stream fs = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{mainDomain}.items.json");

            var dict = await fs.FromJsonAsync<Dictionary<string, BaseRegistryJson>>(codecJsonOptions);
            int registered = 0;

            foreach (var (name, item) in dict)
            {
                var itemName = name.Split(":")[1];
                if (!Enum.TryParse(itemName.Replace("_", ""), true, out Material material))
                    continue;

                Items.Add(material, new Item((short)item.ProtocolId, name, material));
                registered++;
            }

            Logger?.LogDebug($"Successfully registered {registered} items...");
        }

        public static async Task RegisterCodecsAsync()
        {
            using Stream biomes = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{mainDomain}.biome_dimension_codec.json");

            var baseCodec = await biomes.FromJsonAsync<BaseCodec<BiomeCodec>>(codecJsonOptions);

            int registered = 0;
            foreach (var codec in baseCodec.Value)
            {
                Biomes.TryAdd(codec.Name, codec);

                registered++;
            }

            Logger?.LogDebug($"Successfully registered {registered} biomes...");

            using Stream dimensions = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{mainDomain}.default_dimensions.json");

            var dict = await dimensions.FromJsonAsync<Dictionary<string, List<DimensionCodec>>>(codecJsonOptions);

            registered = 0;
            foreach (var (_, values) in dict)
            {
                foreach (var codec in values)
                {
                    Dimensions.TryAdd(codec.Id, codec);

                    Logger?.LogDebug($"Added codec: {codec.Name}:{codec.Id}");
                    registered++;
                }
            }
            Logger?.LogDebug($"Successfully registered {registered} dimensions...");
        }

        public static async Task RegisterTagsAsync()
        {
            int registered = 0;

            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{mainDomain}.tags.json");

            var dict = await stream.FromJsonAsync<Dictionary<string, RawTag>>();

            static void addValues(string tagBase, Tag tag, List<string> values)
            {
                foreach (var value in values)
                {
                    switch (tagBase)
                    {
                        case "items":
                            var item = GetItem(value);

                            tag.Entries.Add(item.Id);
                            break;
                        case "blocks":
                            var block = GetBlock(value);

                            tag.Entries.Add(block.Id);
                            break;
                        case "entity_types":
                            Enum.TryParse<EntityType>(value.TrimMinecraftTag(), true, out var type);
                            tag.Entries.Add((int)type);
                            break;
                        case "fluids":
                            Enum.TryParse<Fluids>(value.TrimMinecraftTag(), true, out var fluid);
                            tag.Entries.Add((int)fluid);
                            break;
                        default:
                            break;
                    }
                }
            }

            foreach (var (name, rawTag) in dict)
            {
                var split = name.Split('/');

                var tagBase = split[0];
                var tagName = split[1];

                if (Tags.ContainsKey(tagBase))
                {
                    var tag = new Tag
                    {
                        Type = tagBase,
                        Name = tagName
                    };
                    addValues(tagBase, tag, rawTag.Values);

                    Logger?.LogDebug($"Registered tag: {name} with {tag.Count} entries");

                    Tags[tagBase].Add(tag);
                }
                else
                {
                    var tag = new Tag
                    {
                        Type = tagBase,
                        Name = tagName
                    };

                    addValues(tagBase, tag, rawTag.Values);

                    Logger?.LogDebug($"Registered tag: {name} with {tag.Count} entries");

                    Tags[tagBase] = new List<Tag> { tag };
                }

                registered++;
            }

            Logger?.LogDebug($"Registered {registered} tags");
        }

        public static async Task RegisterRecipesAsync()
        {
            using Stream fs = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{mainDomain}.recipes.json");

            using var sw = new StreamReader(fs, new UTF8Encoding(false));

            var dict = await fs.FromJsonAsync<Dictionary<string, JsonElement>>();

            foreach (var (name, element) in dict)
            {
                if (!element.TryGetProperty("type", out JsonElement value))
                    throw new InvalidOperationException("Unable to find json property 'type'");

                var type = value.GetString();

                if (!Enum.TryParse<CraftingType>(type.TrimMinecraftTag(), true, out var result))
                    throw new InvalidOperationException("Failed to parse recipe crafting type.");

                var json = element.ToString();

                switch (result)
                {
                    case CraftingType.CraftingShaped:
                        Recipes.Add(name, json.FromJson<ShapedRecipe>());
                        break;
                    case CraftingType.CraftingShapeless:
                        Recipes.Add(name, json.FromJson<ShapelessRecipe>());
                        break;
                    case CraftingType.CraftingSpecialArmordye:
                    case CraftingType.CraftingSpecialBookcloning:
                    case CraftingType.CraftingSpecialMapcloning:
                    case CraftingType.CraftingSpecialMapextending:
                    case CraftingType.CraftingSpecialFireworkRocket:
                    case CraftingType.CraftingSpecialFireworkStar:
                    case CraftingType.CraftingSpecialFireworkStarFade:
                    case CraftingType.CraftingSpecialTippedarrow:
                    case CraftingType.CraftingSpecialBannerduplicate:
                    case CraftingType.CraftingSpecialShielddecoration:
                    case CraftingType.CraftingSpecialShulkerboxcoloring:
                    case CraftingType.CraftingSpecialSuspiciousstew:
                    case CraftingType.CraftingSpecialRepairitem:
                        break;
                    case CraftingType.Smelting:
                    case CraftingType.Blasting:
                    case CraftingType.Smoking:
                    case CraftingType.CampfireCooking:
                        Recipes.Add(name, json.FromJson<SmeltingRecipe>());
                        break;
                    case CraftingType.Stonecutting:
                        Recipes.Add(name, json.FromJson<CuttingRecipe>());
                        break;
                    case CraftingType.Smithing:
                        Recipes.Add(name, json.FromJson<SmithingRecipe>());
                        break;
                    default:
                        break;
                }
            }

            Logger?.LogDebug($"Registered {Recipes.Count} recipes...");
        }

        public static void RegisterCommands(Server server)
        {
            DeclareCommandsPacket = new();
            var index = 0;

            var node = new CommandNode()
            {
                Type = CommandNodeType.Root,
                Index = index
            };

            foreach (var cmd in server.Commands.GetAllCommands())
            {
                var cmdNode = new CommandNode()
                {
                    Index = ++index,
                    Name = cmd.Name,
                    Type = CommandNodeType.Literal
                };

                foreach (var overload in cmd.Overloads.Take(1))
                {
                    var args = overload.GetParameters().Skip(1); // skipping obsidian context
                    if (!args.Any())
                        cmdNode.Type |= CommandNodeType.IsExecutable;

                    CommandNode prev = cmdNode;

                    foreach (var arg in args)
                    {
                        var argNode = new CommandNode()
                        {
                            Index = ++index,
                            Name = arg.Name,
                            Type = CommandNodeType.Argument | CommandNodeType.IsExecutable
                        };

                        Type type = arg.ParameterType;

                        var mctype = server.Commands.FindMinecraftType(type);

                        argNode.Parser = mctype switch
                        {
                            "brigadier:string" => new StringCommandParser(arg.CustomAttributes.Any(x => x.AttributeType == typeof(RemainingAttribute)) ? StringType.GreedyPhrase : StringType.QuotablePhrase),
                            "obsidian:player" => new EntityCommandParser(EntityCommadBitMask.OnlyPlayers),// this is a custom type used by obsidian meaning "only player entities".
                            "brigadier:double" => new DoubleCommandParser(),
                            "brigadier:float" => new FloatCommandParser(),
                            "brigadier:integer" => new IntCommandParser(),
                            "brigadier:long" => new LongCommandParser(),
                            _ => new CommandParser(mctype),
                        };

                        prev.AddChild(argNode);

                        prev = argNode;
                    }
                }

                node.AddChild(cmdNode);
            }

            DeclareCommandsPacket.AddNode(node);
        }

        public static Block GetBlock(Material material) => new Block(material);

        public static Block GetBlock(int id) => new Block(id);

        public static Block GetBlock(string unlocalizedName) =>
            new Block(NumericToBase[Array.IndexOf(BlockNames, unlocalizedName)]);

        public static Item GetItem(int id) => Items.Values.SingleOrDefault(x => x.Id == id);
        public static Item GetItem(Material mat) => Items.GetValueOrDefault(mat);
        public static Item GetItem(string unlocalizedName) =>
            Items.Values.SingleOrDefault(x => x.UnlocalizedName.EqualsIgnoreCase(unlocalizedName));

        public static ItemStack GetSingleItem(Material mat, ItemMeta? meta = null) => new ItemStack(mat, 1, meta);

        public static ItemStack GetSingleItem(string unlocalizedName, ItemMeta? meta = null) => new ItemStack(GetItem(unlocalizedName).Type, 1, meta);

        private class BaseRegistryJson
        {
            public int ProtocolId { get; set; }
        }

        private class BaseCodec<T>
        {
            public string Type { get; set; }

            public List<T> Value { get; set; }
        }
    }

    [DebuggerDisplay("{@base}:{numeric}")]
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
