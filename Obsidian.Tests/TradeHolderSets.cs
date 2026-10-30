using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class TradeHolderSets
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void HolderSetModifiersMatchVanilla(int index)
    {
        using var stream = typeof(Server).Assembly.GetManifestResourceStream(
            "Obsidian.Assets.trade-holder-sets-26.3.json")!;
        using var captured = JsonDocument.Parse(stream);
        var row = captured.RootElement[index];
        var modifier = LootTableJson.ReadModifier(row.GetProperty("modifier").GetRawText());
        var input = VanillaLoot.CapturedCompound("", row.GetProperty("input")).ItemFromNbt()!;
        var expected = VanillaLoot.CapturedCompound("", row.GetProperty("output")).ItemFromNbt()!;
        var random = new LegacyRandomSource(row.GetProperty("seed").GetInt64());

        var output = modifier.Apply(input, new LootContext { Random = random });

        Assert.Equal(expected, output);
        Assert.Equal(expected.Count, output.Count);
        Assert.Equal(row.GetProperty("nextInt").GetInt32(), random.NextInt());
    }

    [Theory]
    [InlineData("set_random_potion", "potion", "minecraft:water", "minecraft:awkward")]
    [InlineData("enchant_randomly", "enchantment", "minecraft:sharpness", "minecraft:smite")]
    [InlineData("enchant_with_levels", "enchantment", "minecraft:sharpness", "minecraft:smite")]
    public void RuntimeHolderSetsFollowTagReloads(string function, string registry, string first, string second)
    {
        // Parse before tags load, as runtime data packs can do, and retain the modifier across both reloads.
        var modifier = LootTableJson.ReadModifier($$"""
            {"type":"minecraft:{{function}}","options":"#holder_test:options","levels":30}
            """);

        try
        {
            foreach (var identifier in new[] { first, second })
            {
                var file = PluginTags.Parse(registry, "holder_test:options", "holder test",
                    Encoding.UTF8.GetBytes($$"""{"replace":true,"values":["{{identifier}}"]}"""));
                PluginTags.Apply([file], NullLogger.Instance);
                var item = registry == "potion" ? ItemsRegistry.Potion : ItemsRegistry.Book;

                var output = modifier.Apply(new ItemStack(item), new LootContext
                {
                    Random = new LegacyRandomSource(12345)
                });

                if (registry == "potion")
                {
                    var potion = output.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
                    Assert.Equal(identifier, "minecraft:" + potion!.Potion!.Value.ToString().ToSnakeCase());
                }
                else
                {
                    var stored = output.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
                        DataComponentType.StoredEnchantments);
                    var enchantment = Assert.Single(stored!.Value);
                    Assert.Equal(identifier, EnchantmentsRegistry.All[enchantment.Id].Identifier);
                }
            }
        }
        finally
        {
            PluginTags.Apply([], NullLogger.Instance);
        }
    }
}
