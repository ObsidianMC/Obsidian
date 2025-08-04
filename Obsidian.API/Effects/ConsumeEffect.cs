namespace Obsidian.API.Effects;
public readonly record struct ConsumeEffect
{
    public required string Type { get; init; }

    public required IConsumeEffect Effect { get; init; }
}
