using Microsoft.Extensions.Logging;
using Obsidian.API.Crafting;
using Obsidian.API.Registry.Codecs;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Chat;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Commands;
using Obsidian.Commands.Parsers;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities.Converters;
using Obsidian.Utilities.Registry.Enums;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Obsidian.Utilities.Registry;

public static partial class Registry
{
    internal static ILogger Logger { get; set; }

    public static CommandsPacket CommandsPacket = new();

    public static readonly Dictionary<string, IRecipe> Recipes = new();

    public static int GlobalBitsPerBlocks { get; internal set; }
    public static int GlobalBitsPerBiomes { get; internal set; }

    public static CodecCollection<int, DimensionCodec> Dimensions { get; } = new("minecraft:dimension_type");
    public static CodecCollection<int, BiomeCodec> Biomes { get; } = new("minecraft:worldgen/biome");

    public static CodecCollection<int, ChatCodec> ChatTypes { get; } = new("minecraft:chat_type");

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
            }
    };

    private static readonly JsonSerializerOptions codecJsonOptions = new(Globals.JsonOptions)
    {
        PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance,
        Converters =
        {
            new IntToBoolConverter(),
        }
    };

    public static async Task RegisterCodecsAsync()
    {
        var asm = Assembly.GetExecutingAssembly()!;

        await using Stream biomes = asm.GetManifestResourceStream($"{mainDomain}.biome_codec.json");

        var baseCodec = await biomes.FromJsonAsync<BaseCodec<BiomeCodec>>(codecJsonOptions);

        int registered = 0;
        foreach (var codec in baseCodec.Value)
        {
            Biomes.TryAdd(codec.Id, codec);

            registered++;
        }

        GlobalBitsPerBiomes = (int)Math.Ceiling(Math.Log2(registered));

        Logger?.LogDebug($"Successfully registered {registered} biomes...");

        await using Stream dimensions = asm.GetManifestResourceStream($"{mainDomain}.default_dimensions.json");

        var dimensionDict = await dimensions.FromJsonAsync<Dictionary<string, List<DimensionCodec>>>(codecJsonOptions);

        registered = 0;
        foreach (var (_, values) in dimensionDict)
        {
            foreach (var codec in values)
            {
                Dimensions.TryAdd(codec.Id, codec);

                Logger?.LogDebug($"Added codec: {codec.Name}:{codec.Id}");
                registered++;
            }
        }

        Logger?.LogDebug($"Successfully registered {registered} dimensions...");

        await using Stream chatTypes = asm.GetManifestResourceStream($"{mainDomain}.chat_type_codec.json");

        var chatTypesDict = await chatTypes.FromJsonAsync<Dictionary<string, List<ChatCodec>>>(codecJsonOptions);

        registered = 0;
        foreach (var (_, values) in chatTypesDict)
        {
            foreach (var codec in values)
            {
                ChatTypes.TryAdd(codec.Id, codec);

                Logger?.LogDebug($"Added codec: {codec.Name}:{codec.Id}");
                registered++;
            }
        }

        Logger?.LogDebug($"Successfully registered {registered} chat types...");
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

            var resourceTag = type.TrimResourceTag();
            if (!Enum.TryParse<CraftingType>(resourceTag, true, out var result))
                throw new InvalidOperationException("Failed to parse recipe crafting type.");

            var json = element.ToString();

            switch (result)
            {
                case CraftingType.CraftingShaped:
                    var shapedRecipe = json.FromJson<ShapedRecipe>();
                    shapedRecipe.Name = name;
                    Recipes.Add(name, shapedRecipe);

                    break;
                case CraftingType.CraftingShapeless:
                    var shapelessRecipe = json.FromJson<ShapelessRecipe>();
                    shapelessRecipe.Name = name;

                    Recipes.Add(name, shapelessRecipe);
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
                    var smeltingRecipe = json.FromJson<SmeltingRecipe>();
                    smeltingRecipe.Name = name;

                    Recipes.Add(name, smeltingRecipe);
                    break;
                case CraftingType.Stonecutting:
                    var stonecuttingRecipe = json.FromJson<CuttingRecipe>();
                    stonecuttingRecipe.Name = name;

                    Recipes.Add(name, stonecuttingRecipe);
                    break;
                case CraftingType.Smithing:
                    var smithingRecipe = json.FromJson<SmithingRecipe>();
                    smithingRecipe.Name = name;

                    Recipes.Add(name, smithingRecipe);
                    break;
                default:
                    break;
            }
        }

        Logger?.LogDebug($"Registered {Recipes.Count} recipes...");
    }

    public static void RegisterCommands(Server server)
    {
        CommandsPacket = new();
        var index = 0;

        var node = new CommandNode()
        {
            Type = CommandNodeType.Root,
            Index = index
        };

        foreach (var cmd in server.CommandsHandler.GetAllCommands())
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

                    var (id, mctype) = server.CommandsHandler.FindMinecraftType(type);

                    argNode.Parser = mctype switch
                    {
                        "brigadier:string" => new StringCommandParser(arg.CustomAttributes.Any(x => x.AttributeType == typeof(RemainingAttribute)) ? StringType.GreedyPhrase : StringType.QuotablePhrase),
                        "obsidian:player" => new EntityCommandParser(EntityCommadBitMask.OnlyPlayers),// this is a custom type used by obsidian meaning "only player entities".
                        "brigadier:double" => new DoubleCommandParser(),
                        "brigadier:float" => new FloatCommandParser(),
                        "brigadier:integer" => new IntCommandParser(),
                        "brigadier:long" => new LongCommandParser(),
                        _ => new CommandParser(id, mctype),
                    };

                    prev.AddChild(argNode);

                    prev = argNode;
                }
            }

            node.AddChild(cmdNode);
        }

        CommandsPacket.AddNode(node);
    }

    public static Block GetBlock(Material material) => new(material);

    public static Block GetBlock(int id) => new(id);

    public static Block GetBlock(string unlocalizedName) =>
        new(BlocksRegistry.NumericToBase[Array.IndexOf(BlocksRegistry.Names, unlocalizedName)]);

    public static Item GetItem(int id) => ItemsRegistry.Items.Values.SingleOrDefault(x => x.Id == id);
    public static Item GetItem(Material mat) => ItemsRegistry.Items.GetValueOrDefault(mat);
    public static Item GetItem(string unlocalizedName) =>
        ItemsRegistry.Items.Values.SingleOrDefault(x => x.UnlocalizedName.EqualsIgnoreCase(unlocalizedName));

    public static ItemStack GetSingleItem(Material mat, ItemMeta? meta = null) => new(mat, 1, meta);

    public static ItemStack GetSingleItem(string unlocalizedName, ItemMeta? meta = null) => new(GetItem(unlocalizedName).Type, 1, meta);

    public static bool TryGetDimensionCodec(int id, [MaybeNullWhen(false)] out DimensionCodec codec) => Dimensions.TryGetValue(id, out codec);
    public static bool TryGetDimensionCodec(string name, [MaybeNullWhen(false)] out DimensionCodec codec)
    {
        var (_, value) = Dimensions.FirstOrDefault(x => x.Value.Name.EqualsIgnoreCase(name));

        if (value is not DimensionCodec dimensionCodec)
        {
            codec = null;
            return false;
        }

        codec = dimensionCodec;

        return true;
    }

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
