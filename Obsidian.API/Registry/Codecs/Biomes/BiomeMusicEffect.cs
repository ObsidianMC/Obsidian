using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeMusicEffect : INbtSerializable
{
    public required bool ReplaceCurrentMusic { get; set; }

    public required int MaxDelay { get; set; }

    public required string Sound { get; set; }

    public required int MinDelay { get; set; }

    public void Write(INbtWriter writer)
    {
        writer.WriteCompoundStart("data");

        writer.WriteBool("replace_current_music", this.ReplaceCurrentMusic);
        writer.WriteInt("max_delay", this.MaxDelay);
        writer.WriteString("sound", this.Sound);
        writer.WriteInt("min_delay", this.MinDelay);

        writer.EndCompound();
    }
}

public sealed record class BiomeMusicEffectData : INbtSerializable
{
    public required BiomeMusicEffect Data { get; set; }

    public int Weight { get; set; }

    public void Write(INbtWriter writer)
    {
        this.Data.Write(writer);

        writer.WriteInt("weight", this.Weight);
    }
}
