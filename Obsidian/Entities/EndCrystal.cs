namespace Obsidian.Entities;

[MinecraftEntity("minecraft:end_crystal")]
public sealed partial class EndCrystal : Entity
{
    public VectorF BeamTarget { get; private set; }

    public bool ShowBottom { get; private set; } = true;
}
