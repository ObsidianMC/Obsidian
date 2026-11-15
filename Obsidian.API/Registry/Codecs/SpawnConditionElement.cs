using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs;
/// <summary>
/// Associates a variant spawn condition with its selection priority.
/// </summary>
public sealed class SpawnConditionElement : INbtSerializable
{
    /// <summary>
    /// Gets or sets the optional condition; <c>null</c> omits the <c>condition</c> compound.
    /// </summary>
    public SpawnCondition? Condition { get; set; }

    /// <summary>
    /// Gets or sets the variant selection priority, serialized as <c>priority</c>.
    /// </summary>
    public required int Priority { get; set; }

    //TODO write the other stuff
    /// <summary>
    /// Writes priority and the condition's type and biomes; structure and range fields are currently omitted.
    /// </summary>
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

    /// <summary>
    /// Describes a variant condition's type and optional biome, structure or range selector.
    /// </summary>
    public sealed class SpawnCondition
    {
        /// <summary>
        /// Gets or sets the spawn condition type identifier, serialized as <c>type</c>.
        /// </summary>
        public required string Type { get; set; }

        /// <summary>
        /// Gets or sets the optional biome selector, serialized as <c>biomes</c>.
        /// </summary>
        public string? Biomes { get; set; }

        /// <summary>
        /// Gets or sets an optional structure selector that the current writer does not emit.
        /// </summary>
        public string? Structures { get; set; }

        /// <summary>
        /// Gets or sets an optional numeric condition range that the current writer does not emit.
        /// </summary>
        public SpawnConditionRange? Range { get; set; }
    }
}

/// <summary>
/// Represents the minimum and maximum of a numeric spawn condition interval.
/// </summary>
public readonly record struct SpawnConditionRange
{
    /// <summary>
    /// Gets the interval's minimum value.
    /// </summary>
    public double Min { get; init; }
    /// <summary>
    /// Gets the interval's maximum value.
    /// </summary>
    public double Max { get; init; }
}
