namespace Obsidian.API;

public interface ILiving : IEntity
{
    public LivingBitMask LivingBitMask { get; set; }

    public uint ActiveEffectColor { get; }

    public bool AmbientPotionEffect { get; set; }
    public bool Alive { get; }

    public int AbsorbedArrows { get; set; }

    public int AbsorbtionAmount { get; set; }

    public Vector BedBlockPosition { get; set; }

    /// <summary>
    /// The dictionary containing all active <see cref="PotionEffect"/> with their respective <see cref="PotionEffectData"/>.
    /// </summary>
    public IReadOnlyDictionary<PotionEffect, PotionEffectData> ActivePotionEffects { get; }

    /// <summary>
    /// Whether the entity has the given <see cref="PotionEffect"/> or not.
    /// </summary>
    /// <param name="potion">The potion effect to be checked.</param>
    /// <returns>True, if the entity has the potion effect.</returns>
    public bool HasPotionEffect(PotionEffect potion);

    /// <summary>
    /// Clears all potion effects of the entity.
    /// </summary>
    public void ClearPotionEffects();

    /// <summary>
    /// Adds the given <see cref="PotionEffect"/> to the entity.
    /// </summary>
    /// <param name="potion">The potion effect to be added.</param>
    /// <param name="duration">The duration of the potion in ticks.</param>
    /// <param name="amplifier">The amplifier of the effect. The given amplifier + 1 will be displayed in the HUD.</param>
    public void AddPotionEffect(PotionEffect potion, int duration, byte amplifier = 0, EntityEffect effect = EntityEffect.None);

    /// <summary>
    /// Removes the given <see cref="PotionEffect"/> from the entity.
    /// </summary>
    /// <param name="potion">The potion effect to be removed.</param>
    public void RemovePotionEffect(PotionEffect potion);
}
