namespace Obsidian.API;

public abstract class ParticleData
{
    public abstract ParticleType ParticleType { get; }

    public virtual void Write(INetStreamWriter writer) { }
}
