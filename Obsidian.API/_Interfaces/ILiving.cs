using Obsidian.API.Effects;

namespace Obsidian.API;

/// <summary>
/// A living entity: one with health and potion effects, like a player or a mob.
/// </summary>
public interface ILiving : IEntity
{
    /// <summary>
    /// Gets or sets the living entity flags sent in its metadata, such as whether it is using an item.
    /// </summary>
    public LivingBitMask LivingBitMask { get; set; }

    /// <summary>
    /// Gets the color of the entity's potion effect particles.
    /// </summary>
    public uint ActiveEffectColor { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the entity's potion effects are ambient (from a beacon), which makes their
    /// particles fainter.
    /// </summary>
    public bool AmbientPotionEffect { get; set; }
    /// <summary>
    /// Gets a value indicating whether the entity's health is above zero.
    /// </summary>
    public bool Alive { get; }

    /// <summary>
    /// Gets or sets the number of arrows shown stuck in the entity.
    /// </summary>
    public int AbsorbedArrows { get; set; }

    /// <summary>
    /// Gets or sets the entity's absorption health, in half-hearts. Negative values are set as 0.
    /// </summary>
    public int AbsorbtionAmount { get; set; }

    /// <summary>
    /// Gets or sets the position of the bed the entity is sleeping in, or <see langword="null"/> if it isn't sleeping.
    /// </summary>
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
