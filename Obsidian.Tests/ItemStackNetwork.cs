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
/// Item stacks as Obsidian sends them, against vanilla 1.21.11's <c>ItemStack.OPTIONAL_STREAM_CODEC</c> encoding of the
/// same stacks (with the enchantment and instrument registries in the order Obsidian sends them).
/// </summary>
public class ItemStackNetwork
{
    public static TheoryData<string, string> Stacks => new()
    {
        // Obsidian's placeholder components (max stack size, rarity, ...) would override the item's own on the client.
        { "diamond_sword", "01a8070000" },
        { "enchanted_book", "01dc09010029012105" },
        { "goat_horn", "01c80a01003b0105" },
        { "splash_potion", "038b0a01003101050100123456010001c8010001010001057761746572" },
        { "map", "01cf090100090a0800097472616e736c617465001a66696c6c65645f6d61702e6275726965645f747265617375726500" }
    };

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
                return new ItemStack(ItemsRegistry.GoatHorn, 1,
                    ComponentBuilder.Instrument with { Value = InstrumentsRegistry.PonderGoatHorn.ToInstrumentData() });
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
