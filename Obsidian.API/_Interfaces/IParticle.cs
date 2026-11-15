namespace Obsidian.API;

/// <summary>
/// A particle effect.
/// </summary>
public interface IParticle
{
    /// <summary>
    /// Gets the particle type's id.
    /// </summary>
    public int Id { get; }
}

internal class BaseParticle : IParticle
{
    public int Id { get; set; }
}
