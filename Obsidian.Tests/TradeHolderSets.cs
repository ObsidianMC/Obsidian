using System.Collections.Immutable;
using System.Linq;
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
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class TradeHolderSets
{
    [Fact]
    public void HolderSetModifiersMatchVanilla()
    {
        using var stream = typeof(Server).Assembly.GetManifestResourceStream(
            "Obsidian.Assets.trade-holder-sets-26.3.json")!;
        using var captured = JsonDocument.Parse(stream);
        var rows = captured.RootElement.EnumerateArray().Select((row, index) => (Row: row, Index: index));

        FixtureCases.AssertAll(rows, row => $"[{row.Index}]", row => AssertHolderSetModifierMatchesVanilla(row.Row));
    }

    private static void AssertHolderSetModifierMatchesVanilla(JsonElement row)
    {
        var modifier = LootTableJson.ReadModifier(row.GetProperty("modifier").GetRawText());
        var input = VanillaLoot.CapturedCompound("", row.GetProperty("input")).ItemFromNbt()!;
        var expected = VanillaLoot.CapturedCompound("", row.GetProperty("output")).ItemFromNbt()!;
        var random = new LegacyRandomSource(row.GetProperty("seed").GetInt64());

        var output = modifier.Apply(input, new LootContext { Random = random });

        Assert.Equal(expected, output);
        Assert.Equal(expected.Count, output.Count);
        Assert.Equal(row.GetProperty("nextInt").GetInt32(), random.NextInt());
    }

    [Fact]
    public void RuntimeHolderSetsFollowTagReloads()
    {
        (string Function, string Registry, string First, string Second)[] cases =
        [
            ("set_random_potion", "potion", "minecraft:water", "minecraft:awkward"),
            ("enchant_randomly", "enchantment", "minecraft:sharpness", "minecraft:smite"),
            ("enchant_with_levels", "enchantment", "minecraft:sharpness", "minecraft:smite")
        ];

        FixtureCases.AssertAll(cases, row => $"{row.Function}, {row.Registry}, {row.First}, {row.Second}",
            row => AssertRuntimeHolderSetsFollowTagReloads(row.Function, row.Registry, row.First, row.Second));
    }

    private static void AssertRuntimeHolderSetsFollowTagReloads(string function, string registry, string first, string second)
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
