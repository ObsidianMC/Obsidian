namespace Obsidian.Entities;

public class EndCrystal : Entity
{
    public VectorF BeamTarget { get; private set; }

    public bool ShowBottom { get; private set; } = true;
}
