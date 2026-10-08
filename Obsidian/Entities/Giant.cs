namespace Obsidian.Entities;

[MinecraftEntity("minecraft:giant")]
public sealed partial class Giant : PathfinderMob
{
    public Giant() => Type = EntityType.Giant;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    protected override async ValueTask TickMobAsync()
    {
        if (Level.LevelData.Difficulty == Difficulty.Peaceful)
            await RemoveAsync();
    }
}
