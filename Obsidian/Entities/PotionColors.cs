namespace Obsidian.Entities;

/// <summary>Vanilla PotionContents' visible-effect color, including its opaque alpha and integer channel rounding.</summary>
internal static class PotionColors
{
    internal static int Mix(IEnumerable<PotionEffectData> effects)
    {
        var red = 0;
        var green = 0;
        var blue = 0;
        var weight = 0;
        foreach (var effect in effects)
        {
            if (!effect.ShowParticles)
                continue;

            var color = EffectColor((PotionEffect)(effect.Id + 1));
            var strength = effect.Amplifier + 1;
            red += (color >> 16 & 255) * strength;
            green += (color >> 8 & 255) * strength;
            blue += (color & 255) * strength;
            weight += strength;
        }

        if (weight == 0)
            return unchecked((int)0xff385dc6);

        return unchecked((int)0xff000000) | red / weight << 16 | green / weight << 8 | blue / weight;
    }

    // 26.3 MobEffects color constants are protocol data: clients receive their mixture in level event 2002/2007.
    private static int EffectColor(PotionEffect effect) => effect switch
    {
        PotionEffect.Speed => 3402751,
        PotionEffect.Slowness => 9154528,
        PotionEffect.Haste => 14270531,
        PotionEffect.MiningFatigue => 4866583,
        PotionEffect.Strength => 16762624,
        PotionEffect.InstantHealth => 16262179,
        PotionEffect.InstantDamage => 11101546,
        PotionEffect.JumpBoost => 16646020,
        PotionEffect.Nausea => 5578058,
        PotionEffect.Regeneration => 13458603,
        PotionEffect.Resistance => 9520880,
        PotionEffect.FireResistance => 16750848,
        PotionEffect.WaterBreathing => 10017472,
        PotionEffect.Invisibility => 16185078,
        PotionEffect.Blindness => 2039587,
        PotionEffect.NightVision => 12779366,
        PotionEffect.Hunger => 5797459,
        PotionEffect.Weakness => 4738376,
        PotionEffect.Poison => 8889187,
        PotionEffect.Wither => 7561558,
        PotionEffect.HealthBoost => 16284963,
        PotionEffect.Absorption => 2445989,
        PotionEffect.Saturation => 16262179,
        PotionEffect.Glowing => 9740385,
        PotionEffect.Levitation => 13565951,
        PotionEffect.Luck => 5882118,
        PotionEffect.BadLuck => 12624973,
        PotionEffect.SlowFalling => 15978425,
        PotionEffect.ConduitPower => 1950417,
        PotionEffect.DolphinsGrace => 8954814,
        PotionEffect.BadOmen => 745784,
        PotionEffect.HeroOfTheVillage => 4521796,
        PotionEffect.Darkness => 2696993,
        PotionEffect.TrialOmen => 1484454,
        PotionEffect.RaidOmen => 14565464,
        PotionEffect.WindCharged => 12438015,
        PotionEffect.Weaving => 7891290,
        PotionEffect.Oozing => 10092451,
        PotionEffect.Infested => 9214860,
        PotionEffect.BreathOfTheNautilus => 65518,
        _ => 0
    };
}
