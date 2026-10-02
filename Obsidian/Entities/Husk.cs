namespace Obsidian.Entities;

[MinecraftEntity("minecraft:husk")]
public sealed partial class Husk : Zombie
{
    public Husk() => Type = EntityType.Husk;
    protected override string? SoundName => "husk";
    internal int WaterTicks { get; set; } = -1;
    internal int ConversionTicks { get; set; } = -1;
    protected override async ValueTask TickMobAsync()
    {
        if (Level.LevelData.Difficulty == Difficulty.Peaceful)
        {
            await RemoveAsync();
            return;
        }
        if (ConversionTicks >= 0)
        {
            if (--ConversionTicks < 0)
                await ConvertToAsync(EntityType.Zombie);
        }
        else if (Terrain.GetBlock((Vector)EyePosition.Floor())?.Material == Material.Water)
        {
            if (++WaterTicks >= 600)
            {
                ConversionTicks = 300;
                SynchronizeMetadata();
            }
        }
        else
            WaterTicks = -1;
    }
    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        var health = target.Health;
        await base.PerformMeleeAttackAsync(target);
        if (target.Health < health && target is Living living && GetEquipment(Obsidian.API.Inventory.EquipmentSlot.MainHand).IsAir)
            living.AddPotionEffect((int)PotionEffect.Hunger - 1, 140 * (int)EffectiveDifficulty, 0);
    }
}
