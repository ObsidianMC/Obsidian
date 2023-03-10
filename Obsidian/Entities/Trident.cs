namespace Obsidian.Entities;

[MinecraftEntity("minecraft:trident")]
public sealed partial class Trident : Arrow
{
    public int LoyaltyLevel { get; private set; }
}
