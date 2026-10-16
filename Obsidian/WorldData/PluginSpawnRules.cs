using Obsidian.API.Entities;
using Obsidian.Entities.AI;
using Obsidian.Plugins;

namespace Obsidian.WorldData;

/// <summary>
/// Natural spawning's rules for a plugin's mob with a spawn placement (<see cref="EntityProperties.SpawnPlacement"/>):
/// where it fits, like vanilla's <c>SpawnPlacementTypes</c>, and its behaviour's spawn predicate.
/// </summary>
internal static class PluginSpawnRules
{
    /// <summary>
    /// Whether the blocks at the mob's feet, below and above them suit the placement; the hitbox was checked free.
    /// </summary>
    public static bool Fits(SpawnPlacementType placement, IBlock feet, IBlock floor, IBlock? above) => placement switch
    {
        SpawnPlacementType.OnGround => !feet.IsLiquid
            && !floor.IsLiquid
            && BlockCollisionShapes.Get(floor).Count > 0,
        SpawnPlacementType.InWater => feet.Is(BlockIds.Water)
            && above is not null
            && (above.Is(BlockIds.Water) || BlockCollisionShapes.Get(above).Count == 0),
        SpawnPlacementType.InLava => feet.Is(BlockIds.Lava),
        _ => true
    };

    /// <summary>
    /// Asks the type's behaviour whether its mob may spawn at <paramref name="position"/>, inside a call into its
    /// plugin; false while the plugin is unloading or unloaded.
    /// </summary>
    public static bool CanSpawn(EntityDefinition definition, ILevel level, Vector position)
    {
        if (definition.Binding is not { Owner: PluginContainer plugin } binding)
            return false;

        if (binding.Behavior is not IEntityBehavior behavior)
            return true;

        using var call = plugin.TryEnter();

        return call is not null && behavior.CanSpawn(level, position);
    }
}
