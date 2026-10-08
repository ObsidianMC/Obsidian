namespace Obsidian.Entities;

[MinecraftEntity("minecraft:cave_spider")]
public sealed partial class CaveSpider : Spider
{
    public CaveSpider() => Type = EntityType.CaveSpider;
    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        var health = target.Health;
        await base.PerformMeleeAttackAsync(target);
        var duration = Level.LevelData.Difficulty switch { Difficulty.Normal => 140, Difficulty.Hard => 300, _ => 0 };
        if (duration > 0 && target.Health < health && target is Living living)
            living.AddPotionEffect((int)PotionEffect.Poison - 1, duration, 0);
    }
}
