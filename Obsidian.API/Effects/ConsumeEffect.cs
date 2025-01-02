namespace Obsidian.API.Effects;
public readonly struct ConsumeEffect
{
    public required string Type { get; init; }

    public required IConsumeEffect Effect { get; init; }
}
