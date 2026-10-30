using Obsidian.Entities;

namespace Obsidian.Registries;

internal static class LootEntityComponents
{
    internal static string? VillagerVariant(IEntity entity)
    {
        int? variant = entity switch
        {
            Villager villager => villager.VillagerType,
            ZombieVillager zombie => zombie.VillagerType,
            _ => null
        };
        if (variant is not int index)
            return null;

        // Vanilla exposes the villager/variant component on both villagers and zombie villagers.
        var type = Villager.BiomeTypes[Math.Clamp(index, 0, Villager.BiomeTypes.Length - 1)];
        return "minecraft:" + type;
    }
}
