using Obsidian.API.Effects;

namespace Obsidian.API;

public interface ILiving : IEntity
{
    public LivingBitMask LivingBitMask { get; set; }

    public uint ActiveEffectColor { get; }

    public bool AmbientPotionEffect { get; set; }
    public bool Alive { get; }

    public int AbsorbedArrows { get; set; }

    public int AbsorbtionAmount { get; set; }

    public Vector? BedBlockPosition { get; set; }

    /// <summary>
    /// The dictionary containing all active <see cref="PotionEffect"/> with their respective <see cref="PotionEffectData"/>.
    /// </summary>
    public IReadOnlyDictionary<int, EffectWithCurrentDuration> ActivePotionEffects { get; }

    /// <summary>
    /// Whether the entity has the given <see cref="PotionEffect"/> or not.
    /// </summary>
    /// <param name="effectId">The potion effect to be checked.</param>
    /// <returns>True, if the entity has the potion effect.</returns>
    public bool HasPotionEffect(int effectId);

    /// <summary>
    /// Clears all potion effects of the entity.
    /// </summary>
    public void ClearPotionEffects();

    /// <summary>
    /// Adds the given <see cref="PotionEffect"/> to the entity.
    /// </summary>
    /// <param name="effectId">The potion effect to be added.</param>
    /// <param name="duration">The duration of the potion in ticks.</param>
    /// <param name="amplifier">The amplifier of the effect. The given amplifier + 1 will be displayed in the HUD.</param>
    /// <param name="effectFlags">Modifies how the effect should be displayed.</param>
    public void AddPotionEffect(int effectId, int duration, int amplifier = 0, EntityEffectFlags effectFlags = EntityEffectFlags.None);

    /// <summary>
    /// Removes the given <see cref="PotionEffect"/> from the entity.
    /// </summary>
    /// <param name="effectId">The potion effect to be removed.</param>
    public void RemovePotionEffect(int effectId);
}
