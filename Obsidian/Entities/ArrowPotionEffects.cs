using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.Entities;

internal static class ArrowPotionEffects
{
    internal static IEnumerable<PotionEffectData> Get(PotionContentsDataComponent? contents)
    {
        if (contents == null) yield break;
        if (contents.Potion is Potion potion)
        {
            var name = potion.ToString();
            var extended = name.StartsWith("Long", StringComparison.Ordinal);
            var strong = name.StartsWith("Strong", StringComparison.Ordinal);
            var kind = extended ? name[4..] : strong ? name[6..] : name;
            var (effect, duration) = kind switch
            {
                "NightVision" => (PotionEffect.NightVision, 3600), "Invisibility" => (PotionEffect.Invisibility, 3600),
                "Leaping" => (PotionEffect.JumpBoost, strong ? 1800 : 3600), "FireResistance" => (PotionEffect.FireResistance, 3600),
                "Swiftness" => (PotionEffect.Speed, strong ? 1800 : 3600), "Slowness" => (PotionEffect.Slowness, strong ? 400 : 1800),
                "WaterBreathing" => (PotionEffect.WaterBreathing, 3600), "Healing" => (PotionEffect.InstantHealth, 1),
                "Harming" => (PotionEffect.InstantDamage, 1), "Poison" => (PotionEffect.Poison, strong ? 432 : 900),
                "Regeneration" => (PotionEffect.Regeneration, strong ? 450 : 900), "Strength" => (PotionEffect.Strength, strong ? 1800 : 3600),
                "Weakness" => (PotionEffect.Weakness, 1800), "Luck" => (PotionEffect.Luck, 6000),
                "SlowFalling" => (PotionEffect.SlowFalling, 1800), "WindCharged" => (PotionEffect.WindCharged, 3600),
                "Weaving" => (PotionEffect.Weaving, 3600), "Oozing" => (PotionEffect.Oozing, 3600),
                "Infested" => (PotionEffect.Infested, 3600), "TurtleMaster" => (PotionEffect.Slowness, 400), _ => ((PotionEffect)0, 0)
            };
            if (duration > 0)
            {
                if (extended) duration = kind switch { "Poison" or "Regeneration" => 1800, "Slowness" or "Weakness" or "SlowFalling" => 4800, "TurtleMaster" => 800, _ => 9600 };
                yield return new() { Id = (int)effect - 1, Duration = duration, Amplifier = kind == "TurtleMaster" ? strong ? 5 : 3 : kind == "Slowness" && strong ? 3 : strong ? 1 : 0 };
                if (kind == "TurtleMaster") yield return new() { Id = (int)PotionEffect.Resistance - 1, Duration = duration, Amplifier = strong ? 3 : 2 };
            }
        }
        foreach (var effect in contents.CustomEffects) yield return effect;
    }
}
