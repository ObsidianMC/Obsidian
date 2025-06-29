using System.Text.Json.Serialization;

namespace Obsidian.API.Registry.Codecs.DamageTypes;
public sealed record class DamageTypeElement
{
    public DeathMessageType? DeathMessageType { get; set; }

    public required float Exhaustion { get; set; }

    public required string MessageId { get; set; }

    public required DamageScaling Scaling { get; set; }
    public DamageEffects? Effects { get; set; }
}
