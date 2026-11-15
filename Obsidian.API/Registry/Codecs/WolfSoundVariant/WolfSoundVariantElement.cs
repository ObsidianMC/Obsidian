using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.WolfSoundVariant;
/// <summary>
/// Contains separate adult and puppy sound sets for a wolf sound variant.
/// </summary>
public sealed class WolfSoundVariantElement
{
    /// <summary>
    /// Gets or sets the adult sound set serialized as <c>adult_sounds</c>.
    /// </summary>
    public WolfSounds AdultSounds { get; set; }

    /// <summary>
    /// Gets or sets the puppy sound set serialized as <c>baby_sounds</c>.
    /// </summary>
    public WolfSounds BabySounds { get; set; }
}

/// <summary>The sounds of a wolf sound variant, for adults or for puppies.</summary>
public sealed class WolfSounds
{
    /// <summary>
    /// Gets or sets the ambient sound event identifier, serialized as <c>ambient_sound</c>.
    /// </summary>
    public string AmbientSound { get; set; }
    /// <summary>
    /// Gets or sets the death sound event identifier, serialized as <c>death_sound</c>.
    /// </summary>
    public string DeathSound { get; set; }
    /// <summary>
    /// Gets or sets the growling sound event identifier, serialized as <c>growl_sound</c>.
    /// </summary>
    public string GrowlSound { get; set; }
    /// <summary>
    /// Gets or sets the hurt sound event identifier, serialized as <c>hurt_sound</c>.
    /// </summary>
    public string HurtSound { get; set; }
    /// <summary>
    /// Gets or sets the panting sound event identifier, serialized as <c>pant_sound</c>.
    /// </summary>
    public string PantSound { get; set; }
    /// <summary>
    /// Gets or sets the footstep sound event identifier, serialized as <c>step_sound</c>.
    /// </summary>
    public string StepSound { get; set; }
    /// <summary>
    /// Gets or sets the whining sound event identifier, serialized as <c>whine_sound</c>.
    /// </summary>
    public string WhineSound { get; set; }

    internal void Write(INbtWriter writer, string name)
    {
        writer.WriteCompoundStart(name);
        writer.WriteString("ambient_sound", this.AmbientSound);
        writer.WriteString("death_sound", this.DeathSound);
        writer.WriteString("growl_sound", this.GrowlSound);
        writer.WriteString("hurt_sound", this.HurtSound);
        writer.WriteString("pant_sound", this.PantSound);
        writer.WriteString("step_sound", this.StepSound);
        writer.WriteString("whine_sound", this.WhineSound);
        writer.EndCompound();
    }
}
