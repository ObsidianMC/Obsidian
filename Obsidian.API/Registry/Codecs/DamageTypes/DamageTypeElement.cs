namespace Obsidian.API.Registry.Codecs.DamageTypes;
public sealed record class DamageTypeElement
{
    public DeathMessageType? DeathMessageType { get; set; }

    public float Exhaustion { get; set; }

    public string MessageId { get; set; }

    public DamageScaling Scaling { get; set; }

    public DamageEffects? Effects { get; set; }

    internal DamageTypeElement() { }
}
