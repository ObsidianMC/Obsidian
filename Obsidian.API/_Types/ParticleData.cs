namespace Obsidian.API;

/// <summary>A particle type together with the extra data some particles need, such as a dust color.</summary>
public abstract class ParticleData
{
    /// <summary>Gets the particle type.</summary>
    public abstract ParticleType ParticleType { get; }

    /// <summary>Writes the particle's extra data to a network stream. The default writes nothing.</summary>
    public virtual void Write(INetStreamWriter writer) { }
}
