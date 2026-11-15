using System.Text.Json.Serialization;

namespace Obsidian.API.Registry.Codecs.DamageTypes;
/// <summary>
/// Defines death message selection, exhaustion and difficulty scaling for a damage type.
/// </summary>
public sealed record class DamageTypeElement
{
    /// <summary>
    /// Gets or sets the optional death message format; <c>null</c> uses the default format.
    /// </summary>
    public DeathMessageType? DeathMessageType { get; set; }

    /// <summary>
    /// Gets or sets the exhaustion applied by this damage type, serialized as <c>exhaustion</c>.
    /// </summary>
    public required float Exhaustion { get; set; }

    /// <summary>
    /// Gets or sets the death-message translation suffix, serialized as <c>message_id</c>.
    /// </summary>
    public required string MessageId { get; set; }

    /// <summary>
    /// Gets or sets how damage scales with difficulty, serialized as <c>scaling</c>.
    /// </summary>
    public required DamageScaling Scaling { get; set; }
    /// <summary>
    /// Gets or sets the optional hurt effect; <c>null</c> uses the default hurt effect.
    /// </summary>
    public DamageEffects? Effects { get; set; }
}
