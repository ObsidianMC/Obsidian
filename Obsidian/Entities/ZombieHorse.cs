namespace Obsidian.Entities;

[MinecraftEntity("minecraft:zombie_horse")]
public sealed partial class ZombieHorse : AbstractHorse
{
    public ZombieHorse() => Type = EntityType.ZombieHorse;
    protected override bool UsesAi => true;

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (Level.DimensionName == "minecraft:overworld" && Level.DayTime is >= 0 and < 12000 &&
            !Level.LevelData.Raining && !Level.LevelData.Thundering && !InWater &&
            Random.NextSingle() * 30 < 1.2f && Terrain.GetSkyLight((Vector)EyePosition.Floor()) == 15 &&
            GetEquipment(Obsidian.API.Inventory.EquipmentSlot.Body).IsAir)
            Ignite(8);
    }
}
