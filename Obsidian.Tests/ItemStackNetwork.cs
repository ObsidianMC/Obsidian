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
    [Fact]
    public void ItemDefaultsArentSent() =>
        // Obsidian's placeholder components (max stack size 64, common rarity, ...) would override a sword's own.
        Assert.Equal("01a807" + "0000", Encode(new ItemStack(ItemsRegistry.DiamondSword)));

    [Fact]
    public void StoredEnchantmentsMatchVanilla()
    {
        var book = new ItemStack(ItemsRegistry.EnchantedBook);
        EnchantmentHelper.Enchant(book, EnchantmentsRegistry.Sharpness, 5);

        Assert.Equal("01dc09010029012105", Encode(book));
    }

    [Fact]
    public void InstrumentMatchesVanilla() =>
        Assert.Equal("01c80a01003b0105", Encode(new ItemStack(ItemsRegistry.GoatHorn, 1,
            ComponentBuilder.Instrument with { Value = InstrumentsRegistry.PonderGoatHorn.ToInstrumentData() })));

    [Fact]
    public void PotionContentsMatchVanilla() =>
        Assert.Equal("038b0a01003101050100123456010001c8010001010001057761746572", Encode(new ItemStack(ItemsRegistry.SplashPotion, 3,
            new PotionContentsDataComponent
            {
                Potion = Potion.LongNightVision,
                CustomColor = 0x123456,
                CustomEffects = [new PotionEffectData { Id = (int)MobEffect.Speed, Amplifier = 1, Duration = 200, ShowParticles = true, ShowIcon = true }],
                CustomName = "water"
            })));

    [Fact]
    public void SuspiciousStewEffectsMatchVanilla() =>
        Assert.Equal("01bc0a01003302160712f001", Encode(new ItemStack(ItemsRegistry.SuspiciousStew, 1, ComponentBuilder.SuspiciousStewEffects with
        {
            Value = [new SuspiciousStewEffect { EffectId = (int)MobEffect.Saturation, Duration = 7 }, new SuspiciousStewEffect { EffectId = (int)MobEffect.Poison, Duration = 240 }]
        })));

    [Fact]
    public void OminousBottleAmplifierMatchesVanilla() =>
        Assert.Equal("02e00b01003d04", Encode(new ItemStack(ItemsRegistry.OminousBottle, 2, ComponentBuilder.OminousBottleAmplifier with { Value = 4 })));

    [Fact]
    public void ItemNameMatchesVanilla() =>
        Assert.Equal("01cf090100090a0800097472616e736c617465001a66696c6c65645f6d61702e6275726965645f747265617375726500",
            Encode(new ItemStack(ItemsRegistry.Map, 1, ComponentBuilder.ItemName with { Value = new ChatMessage { Translate = "filled_map.buried_treasure" } })));

    private static string Encode(ItemStack stack)
    {
        var buffer = new NetworkBuffer();
        buffer.WriteItemStack(stack);
        return Convert.ToHexStringLower(buffer.Data.AsSpan(0, buffer.Offset));
    }
}
