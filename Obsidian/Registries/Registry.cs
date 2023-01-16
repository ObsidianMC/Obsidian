using Microsoft.Extensions.Logging;

namespace Obsidian.Registries;

public static partial class Registry
{
    internal static ILogger Logger { get; set; }

    //public static async Task RegisterRecipesAsync()
    //{
    //    using var fs = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{mainDomain}.recipes.json");

    //    using var sw = new StreamReader(fs, new UTF8Encoding(false));

    //    var dict = await fs.FromJsonAsync<Dictionary<string, JsonElement>>();

    //    foreach (var (name, element) in dict)
    //    {
    //        if (!element.TryGetProperty("type", out var value))
    //            throw new InvalidOperationException("Unable to find json property 'type'");

    //        var type = value.GetString();

    //        var resourceTag = type.TrimResourceTag();
    //        if (!Enum.TryParse<CraftingType>(resourceTag, true, out var result))
    //            throw new InvalidOperationException("Failed to parse recipe crafting type.");

    //        var json = element.ToString();

    //        switch (result)
    //        {
    //            case CraftingType.CraftingShaped:
    //                var shapedRecipe = json.FromJson<ShapedRecipe>();
    //                shapedRecipe.Identifier = name;
    //                Recipes.Add(name, shapedRecipe);

    //                break;
    //            case CraftingType.CraftingShapeless:
    //                var shapelessRecipe = json.FromJson<ShapelessRecipe>();
    //                shapelessRecipe.Identifier = name;

    //                Recipes.Add(name, shapelessRecipe);
    //                break;
    //            case CraftingType.CraftingSpecialArmordye:
    //            case CraftingType.CraftingSpecialBookcloning:
    //            case CraftingType.CraftingSpecialMapcloning:
    //            case CraftingType.CraftingSpecialMapextending:
    //            case CraftingType.CraftingSpecialFireworkRocket:
    //            case CraftingType.CraftingSpecialFireworkStar:
    //            case CraftingType.CraftingSpecialFireworkStarFade:
    //            case CraftingType.CraftingSpecialTippedarrow:
    //            case CraftingType.CraftingSpecialBannerduplicate:
    //            case CraftingType.CraftingSpecialShielddecoration:
    //            case CraftingType.CraftingSpecialShulkerboxcoloring:
    //            case CraftingType.CraftingSpecialSuspiciousstew:
    //            case CraftingType.CraftingSpecialRepairitem:
    //                break;
    //            case CraftingType.Smelting:
    //            case CraftingType.Blasting:
    //            case CraftingType.Smoking:
    //            case CraftingType.CampfireCooking:
    //                var smeltingRecipe = json.FromJson<SmeltingRecipe>();
    //                smeltingRecipe.Identifier = name;

    //                Recipes.Add(name, smeltingRecipe);
    //                break;
    //            case CraftingType.Stonecutting:
    //                var stonecuttingRecipe = json.FromJson<CuttingRecipe>();
    //                stonecuttingRecipe.Identifier = name;

    //                Recipes.Add(name, stonecuttingRecipe);
    //                break;
    //            case CraftingType.Smithing:
    //                var smithingRecipe = json.FromJson<SmithingRecipe>();
    //                smithingRecipe.Identifier = name;

    //                Recipes.Add(name, smithingRecipe);
    //                break;
    //            default:
    //                break;
    //        }
    //    }

    //    Logger?.LogDebug($"Registered {Recipes.Count} recipes...");
    //}

  
}
