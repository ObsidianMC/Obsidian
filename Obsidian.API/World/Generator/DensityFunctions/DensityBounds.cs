namespace Obsidian.API.World.Generator.DensityFunctions;

/// <summary>
/// Lazily computed value range of a density function.
/// </summary>
/// <remarks>
/// Trees are immutable once built, so composite functions cache their bounds here instead of
/// recomputing them recursively on every evaluation. It's a class so publishing it is atomic.
/// </remarks>
internal sealed record DensityBounds(double Min, double Max);
