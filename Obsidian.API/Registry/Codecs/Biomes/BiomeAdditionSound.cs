using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeAdditionSound : INbtSerializable
{
    public required string Sound { get; set; }

    public required double TickChance { get; set; }

    public void Write(INbtWriter writer)
    {
        writer.WriteCompoundStart("additions_sound");

        writer.WriteString("sound", this.Sound);
        writer.WriteDouble("tick_chance", this.TickChance);

        writer.EndCompound();
    }
}

public sealed record class BiomeMoodSound : INbtSerializable
{
    public required string Sound { get; set; }

    public required double Offset { get; set; }

    public required int TickDelay { get; set; }
    public required int BlockSearchExtent { get; set; }

    public void Write(INbtWriter writer)
    {
        writer.WriteCompoundStart("mood_sound");

        writer.WriteString("sound", this.Sound);
        writer.WriteDouble("offset", this.Offset);
        writer.WriteInt("tick_delay", this.TickDelay);
        writer.WriteInt("block_search_extent", this.BlockSearchExtent);

        writer.EndCompound();
    }
}
