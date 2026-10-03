namespace Obsidian.API.World.Features;

/// <summary>
/// Vertical range the chunk generator works in, used to resolve <see cref="VerticalAnchor"/>s.
/// </summary>
public readonly record struct WorldGenerationContext(int MinY, int Height);
