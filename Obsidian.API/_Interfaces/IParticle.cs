namespace Obsidian.API;

public interface IParticle
{
    public int Id { get; }
}

internal class BaseParticle : IParticle
{
    public int Id { get; set; }
}
