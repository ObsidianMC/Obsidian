using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs;
public sealed class SpawnConditionElement : INbtSerializable
{
    public SpawnCondition? Condition { get; set; }

    public required int Priority { get; set; }

    //TODO write the other stuff
    public void Write(INbtWriter writer)
    {
        if (this.Condition is SpawnCondition condition)
        {
            writer.WriteCompoundStart("condition");

            writer.WriteString("type", condition.Type);
            writer.WriteString("biomes", condition.Biomes);

            writer.EndCompound();
        }

        writer.WriteInt("priority", this.Priority);
    }

    public sealed class SpawnCondition
    {
        public required string Type { get; set; }

        public string? Biomes { get; set; }

        public string? Structures { get; set; }

        public SpawnConditionRange? Range { get; set; }
    }
}

public readonly record struct SpawnConditionRange
{
    public double Min { get; init; }
    public double Max { get; init; }
}
