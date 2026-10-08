using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:endermite")]
public sealed partial class Endermite : PathfinderMob
{
    private const int MaximumLifetime = 2400;
    public int Lifetime { get; internal set; }
    public Endermite() => Type = EntityType.Endermite;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override string? SoundName => "endermite";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 3;

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new FloatGoal(this));
        actions.AddGoal(2, new MeleeAttackGoal(this));
        actions.AddGoal(3, new RandomStrollGoal(this, 1));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is IPlayer));
    }

    protected override ValueTask TickMobAsync() => !PersistenceRequired && ++Lifetime >= MaximumLifetime ? RemoveAsync() : default;
    protected override void WriteAdditionalSave(INbtWriter writer) => writer.WriteInt("Lifetime", Lifetime);
    protected override void ReadAdditionalSave(NbtCompound tag) =>
        Lifetime = Math.Max(0, tag.TryGetTagValue<int>("Lifetime", out var lifetime) ? lifetime : 0);
}
