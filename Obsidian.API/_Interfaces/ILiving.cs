namespace Obsidian.API;

public interface ILiving : IEntity
{
    public LivingBitMask LivingBitMask { get; set; }

    public float Health { get; set; }

    public uint ActiveEffectColor { get; }

    public bool AmbientPotionEffect { get; set; }
    public bool Alive { get; }

    public int AbsorbedArrows { get; set; }

    public int AbsorbtionAmount { get; set; }

    public Vector BedBlockPosition { get; set; }
    
    /// <summary>
    /// The list containing all currently active <see cref="PotionEffect"/>s.
    /// </summary>
    public IReadOnlyList<PotionEffect> ActivePotionEffects { get; }

    /// <summary>
    /// Whether the entity has the given <see cref="PotionEffect"/> or not.
    /// </summary>
    /// <param name="potion">The potion effect to be checked.</param>
    /// <returns>True, if the entity has the potion effect.</returns>
    public bool HasPotionEffect(PotionEffect potion);

    /// <summary>
    /// Clears all potion effects of the entity.
    /// </summary>
    public Task ClearPotionEffects();

    /// <summary>
    /// Adds the given <see cref="PotionEffect"/> to the entity.
    /// </summary>
    /// <param name="potion">The potion effect to be added.</param>
    /// <param name="duration">The duration of the potion in ticks.</param>
    public Task AddPotionEffectAsync(PotionEffect potion, int duration);

    /// <summary>
    /// Adds the given <see cref="PotionEffect"/> to the entity.
    /// </summary>
    /// <param name="potion">The potion effect to be added.</param>
    /// <param name="duration">The duration of the potion in ticks.</param>
    /// <param name="amplifier">The amplifier of the effect. The given amplifier + 1 will be displayed in the HUD.</param>
    public Task AddPotionEffectAsync(PotionEffect potion, int duration, byte amplifier);

    /// <summary>
    /// Adds the given <see cref="PotionEffect"/> to the entity.
    /// </summary>
    /// <param name="potion">The potion effect to be added.</param>
    /// <param name="duration">The duration of the potion in ticks.</param>
    /// <param name="amplifier">The amplifier of the effect. The given amplifier + 1 will be displayed in the HUD.</param>
    /// <param name="showParticles">Whether to show the particles or not.</param>
    /// <param name="showIcon">Whether to show the icon on the client or not.</param>
    /// <param name="isAmbient">Whether the potion is emitted by ambient source e.g. the beacon. The icon has a blue border in the HUD if its ambient.</param>
    public Task AddPotionEffectAsync(PotionEffect potion, int duration, byte amplifier, bool showParticles,
        bool showIcon, bool isAmbient);

    /// <summary>
    /// Removes the given <see cref="PotionEffect"/> from the entity.
    /// </summary>
    /// <param name="potion">The potion effect to be removed.</param>
    public Task RemovePotionEffectAsync(PotionEffect potion);
}
