using Obsidian.API.Utilities;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.DamageTypes;

/// <summary>
/// Represents a damage type registry entry and its synchronized element data.
/// </summary>
public sealed class DamageTypeCodec : ICodec
{
    /// <summary>
    /// Gets the entry's namespaced registry identifier.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the numeric registry ID used on the network.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// The entry's data. A plugin's entry gets a new element when its plugin reloads, and keeps its instance and id.
    /// </summary>
    public required DamageTypeElement Element { get; set; }

    // Matches vanilla's DamageType.DIRECT_CODEC, which clients decode damage types with: the default effects (hurt)
    // and death message type (default) are left out.
    /// <summary>
    /// Writes this entry's damage type fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer)
    {
        var element = this.Element;

        writer.WriteString("message_id", element.MessageId);
        writer.WriteString("scaling", element.Scaling.ToString().ToSnakeCase());
        writer.WriteFloat("exhaustion", element.Exhaustion);

        if (element.Effects is DamageEffects effects && effects != DamageEffects.Hurt)
            writer.WriteString("effects", effects.ToString().ToSnakeCase());

        if (element.DeathMessageType is DeathMessageType deathMessageType
            && deathMessageType != DeathMessageType.Default)
        {
            writer.WriteString("death_message_type", deathMessageType.ToString().ToSnakeCase());
        }
    }
}
