namespace Obsidian.API.Particles;

public class VibrationParticle : IParticle
{
    public int Id => 37;

    public VectorF Origin { get; set; }

    public VectorF Destination { get; set; }

    public int Ticks { get; set; }
}
