namespace Obsidian.Entities;

[MinecraftEntity("minecraft:fishing_bobber")]
public sealed partial class FishingHook : Entity
{
    // Like vanilla, bobbers never use portals.
    internal override bool CanUsePortals => false;
}
