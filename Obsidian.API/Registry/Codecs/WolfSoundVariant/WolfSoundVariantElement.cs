using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.WolfSoundVariant;
public sealed class WolfSoundVariantElement
{
    public WolfSounds AdultSounds { get; set; }

    public WolfSounds BabySounds { get; set; }
}

/// <summary>The sounds of a wolf sound variant, for adults or for puppies.</summary>
public sealed class WolfSounds
{
    public string AmbientSound { get; set; }
    public string DeathSound { get; set; }
    public string GrowlSound { get; set; }
    public string HurtSound { get; set; }
    public string PantSound { get; set; }
    public string StepSound { get; set; }
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
