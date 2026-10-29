using Obsidian.API;
using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Net;
using System;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Item stacks as Obsidian sends them, against vanilla 26.3's <c>ItemStack.OPTIONAL_STREAM_CODEC</c> encoding of the
/// same stacks (with the enchantment and instrument registries in the order Obsidian sends them).
/// </summary>
public class ItemStackNetwork
{
    public static TheoryData<string, string> Stacks
    {
        get
        {
            using var fixture = ItemComponentWire.Fixture();
            var rows = new TheoryData<string, string>();
            foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
            {
                var name = row.GetProperty("name").GetString()!;
                if (name.StartsWith("network_", StringComparison.Ordinal))
                    rows.Add(name["network_".Length..], row.GetProperty("wire").GetString()!);
            }
            Assert.Equal(5, rows.Count);
            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Stacks))]
    public void StacksMatchVanilla(string stack, string expected) => Assert.Equal(expected, Encode(Create(stack)));

    private static ItemStack Create(string stack)
    {
        switch (stack)
        {
            case "enchanted_book":
                var book = new ItemStack(ItemsRegistry.EnchantedBook);
                EnchantmentHelper.Enchant(book, EnchantmentsRegistry.Sharpness, 5);
                return book;
            case "goat_horn":
                // Use a nondefault instrument so vanilla retains the explicit component patch.
                return new ItemStack(ItemsRegistry.GoatHorn, 1,
                    ComponentBuilder.Instrument with { Value = InstrumentsRegistry.SingGoatHorn.ToInstrumentData() });
            case "splash_potion":
                return new ItemStack(ItemsRegistry.SplashPotion, 3, new PotionContentsDataComponent
                {
                    Potion = Potion.LongNightVision,
                    CustomColor = 0x123456,
                    CustomEffects =
                    [
                        new PotionEffectData { Id = (int)MobEffect.Speed, Amplifier = 1, Duration = 200, ShowParticles = true, ShowIcon = true }
                    ],
                    CustomName = "water"
                });
            case "map":
                return new ItemStack(ItemsRegistry.Map, 1,
                    ComponentBuilder.ItemName with { Value = new ChatMessage { Translate = "filled_map.buried_treasure" } });
            default:
                return new ItemStack(ItemsRegistry.DiamondSword);
        }
    }

    private static string Encode(ItemStack stack)
    {
        var buffer = new NetworkBuffer();
        buffer.WriteItemStack(stack);
        return Convert.ToHexStringLower(buffer.GetBuffer().AsSpan(0, buffer.Offset));
    }
}
