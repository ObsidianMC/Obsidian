namespace Obsidian.Entities;

[MinecraftEntity("minecraft:arrow")]
public partial class Arrow : Entity
{
    public bool Crit { get; private set; }
    public bool NoClip { get; private set; }
}
