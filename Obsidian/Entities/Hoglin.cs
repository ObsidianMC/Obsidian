using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:hoglin")]
public sealed partial class Hoglin : FarmAnimal
{
    public bool IsImmuneToZombification { get; set; }
    public bool CannotBeHunted { get; set; }
    internal int TimeInOverworld { get; set; }
    public Hoglin() => Type = EntityType.Hoglin;
    internal override bool PanicsWhenHurt => false;
    protected override bool CanDespawn => true;
    protected override string? SoundName => "hoglin";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => IsBaby ? 0 : 5;
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.CrimsonFungus };
    private bool IsConverting => !IsImmuneToZombification &&
        (CodecRegistry.TryGetDimension(Level.DimensionName, out var dimension) ? !dimension.Element.PiglinSafe : Level.DimensionName != "minecraft:the_nether");

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(1, new HoglinAvoidFungusGoal(this));
        actions.AddGoal(1, new AvoidEntityGoal(this, target => IsBaby && ReferenceEquals(target, LastAttacker) && AiTick - LastHurtTick < 100, 8, 1.3f));
        actions.AddGoal(3, new HoglinMeleeGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => !IsBaby && LoveTicks == 0 &&
            Level.LevelData.Difficulty != Difficulty.Peaceful && target is IPlayer));
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (!IsConverting) { TimeInOverworld = 0; return; }
        if (++TimeInOverworld > 300)
        {
            PlayMobSound("converted_to_zombified");
            var zoglin = (Zoglin)await ConvertToAsync(EntityType.Zoglin);
            zoglin.AddPotionEffect((int)PotionEffect.Nausea - 1, 200);
        }
    }
    protected internal override ValueTask PerformMeleeAttackAsync(IEntity target) => HoglinAttack.PerformAsync(this, target, IsBaby);
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (!IsBaby)
        {
            DropItem(Burning ? Material.CookedPorkchop : Material.Porkchop, Random.Next(2, 5));
            DropItem(Material.Leather, Random.Next(2));
        }
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsConverting && !MobBitMask.HasFlag(MobBitmask.NoAi));
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteBool("IsImmuneToZombification", IsImmuneToZombification);
        writer.WriteBool("CannotBeHunted", CannotBeHunted);
        writer.WriteInt("TimeInOverworld", TimeInOverworld);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        IsImmuneToZombification = tag.TryGetBool("IsImmuneToZombification", out var immune) && immune;
        CannotBeHunted = tag.TryGetBool("CannotBeHunted", out var hunted) && hunted;
        TimeInOverworld = Math.Max(0, tag.TryGetTagValue<int>("TimeInOverworld", out var ticks) ? ticks : 0);
    }
}

internal sealed class HoglinMeleeGoal(Hoglin hoglin) : MeleeAttackGoal(hoglin)
{
    private bool CanAttack => hoglin.Level.LevelData.Difficulty != Difficulty.Peaceful || hoglin.AttackTarget is not IPlayer;
    public override bool CanUse() => CanAttack && base.CanUse();
    public override bool CanContinue() => CanAttack && base.CanContinue();
}

internal sealed class HoglinAvoidFungusGoal(Hoglin hoglin) : NavigationGoal(hoglin, 1.3f)
{
    private VectorD destination;
    public override bool CanUse()
    {
        if (hoglin.Random.Next(20) != 0) return false;
        var origin = (Vector)hoglin.Position.Floor();
        for (var x = -8; x <= 8; x++)
        for (var y = -4; y <= 4; y++)
        for (var z = -8; z <= 8; z++)
        {
            var point = origin + new Vector(x, y, z);
            if (hoglin.Terrain.GetBlock(point)?.Material != Material.WarpedFungus ||
                RandomPosition.Find(hoglin, 8, (VectorD)point) is not VectorD away ||
                (away - (VectorD)point).MagnitudeSquared() <= (hoglin.Position - (VectorD)point).MagnitudeSquared()) continue;
            destination = away;
            return true;
        }
        return false;
    }
    public override void Start() { hoglin.AttackTarget = null; MoveTo(destination); }
    public override bool CanContinue() => Navigation.IsNavigating;
}

[MinecraftEntity("minecraft:zoglin")]
public sealed partial class Zoglin : PathfinderMob
{
    public Zoglin() => Type = EntityType.Zoglin;
    public bool IsBaby { get; set; }
    protected override float DimensionScale => IsBaby ? 0.5f : 1;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override string? SoundName => "zoglin";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new FloatGoal(this));
        actions.AddGoal(2, new MeleeAttackGoal(this));
        actions.AddGoal(7, new RandomStrollGoal(this, 1));
        actions.AddGoal(8, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(9, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is Living && target.Type is not (EntityType.Zoglin or EntityType.Creeper)));
    }
    protected internal override ValueTask PerformMeleeAttackAsync(IEntity target) => HoglinAttack.PerformAsync(this, target, IsBaby);
    protected override ValueTask OnDeathAsync(IEntity source) { DropItem(Material.RottenFlesh, Random.Next(1, 4)); return default; }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsBaby);
    }
    protected override void WriteAdditionalSave(INbtWriter writer) => writer.WriteBool("IsBaby", IsBaby);
    protected override void ReadAdditionalSave(NbtCompound tag) => IsBaby = tag.TryGetBool("IsBaby", out var baby) && baby;
}

internal static class HoglinAttack
{
    internal static async ValueTask PerformAsync(Mob mob, IEntity target, bool baby)
    {
        mob.SendEntityEvent(4);
        mob.PlayMobSound("attack");
        var strength = mob.AttackDamage;
        var damage = baby ? 0.5f : strength / 2 + mob.Random.Next(Math.Max(1, (int)strength));
        if (target is IPlayer)
            damage = mob.Level.LevelData.Difficulty switch
            {
                Difficulty.Peaceful => 0,
                Difficulty.Easy => MathF.Min(damage / 2 + 1, damage),
                Difficulty.Hard => damage * 1.5f,
                _ => damage
            };
        var health = target.Health;
        await target.DamageAsync(mob, damage);
        if (baby || target.Health >= health || target is not Entity entity) return;
        var resistance = entity.GetAttributeValue("minecraft:generic.knockback_resistance");
        var knockback = mob.GetAttributeValue("minecraft:generic.attack_knockback") - resistance;
        var delta = target.Position - mob.Position;
        var distance = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        if (knockback <= 0 || distance < 0.0001f) return;
        var angle = mob.Random.Next(-10, 11) * Math.PI / 180;
        var force = knockback * (mob.Random.NextSingle() * 0.5f + 0.2f);
        entity.Motion += new VectorD((delta.X * Math.Cos(angle) - delta.Z * Math.Sin(angle)) / distance * force,
            knockback * mob.Random.NextSingle() * 0.5f, (delta.X * Math.Sin(angle) + delta.Z * Math.Cos(angle)) / distance * force);
    }
}
