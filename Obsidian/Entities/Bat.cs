namespace Obsidian.Entities;

[MinecraftEntity("minecraft:bat")]
public sealed partial class Bat : Ambient
{
    public bool IsHanging { get; }
}
