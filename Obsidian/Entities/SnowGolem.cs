using Obsidian.Entities.AI;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:snow_golem")]
public sealed partial class SnowGolem : PathfinderMob
{
    public bool Pumpkin { get; set; } = true;
    public SnowGolem() { Type = EntityType.SnowGolem; PersistenceRequired = true; }
    protected override bool UsesAi => true;
    protected override bool WaterSensitive => true;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "snow_golem";
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new SnowGolemAttackGoal(this));
        actions.AddGoal(2, new RandomStrollGoal(this, 1));
        actions.AddGoal(3, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(4, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, entity => entity is Mob mob &&
            (mob.Hostile || entity.Type is EntityType.Zombie or EntityType.Husk or EntityType.Skeleton or EntityType.Stray or
                EntityType.Bogged or EntityType.Parched or EntityType.Creeper or EntityType.Slime), false));
    }
    protected override async ValueTask TickMobAsync()
    {
        var point = (Vector)Position.Floor();
        var temperature = Terrain.GetTemperature(point);
        if (temperature > 1)
            await DamageEnvironmentAsync(1);
        if (!Alive || temperature >= 0.8f || MobBitMask.HasFlag(MobBitmask.NoAi))
            return;
        for (var index = 0; index < 4; index++)
        {
            var snow = (Vector)(Position + new VectorF((index % 2 * 2 - 1) * 0.25f, 0, (index / 2 * 2 - 1) * 0.25f)).Floor();
            if (Terrain.GetBlock(snow)?.IsAir == true && Terrain.GetBlock(new Vector(snow.X, snow.Y - 1, snow.Z)) is { } floor &&
                floor.IsCollisionShapeFullBlock() && !floor.IsLiquid)
                await Level.SetBlockAsync(snow, BlocksRegistry.Get(Material.Snow), true);
        }
    }
    internal override async ValueTask InteractAsync(IPlayer player, Hand hand)
    {
        if (!Alive || player.Level != Level || player.Gamemode == Gamemode.Spectator || !IsInRange(player, 3) || !CanSee(player))
            return;
        var item = hand == Hand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (Pumpkin && item?.Type == Material.Shears)
        {
            Pumpkin = false;
            DropItem(Material.CarvedPumpkin, 1);
            PlayMobSound("shear");
            await DamageInteractionToolAsync(player, hand);
            SynchronizeMetadata();
        }
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.Snowball, Random.Next(16));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Byte);
        writer.WriteByte((byte)(Pumpkin ? 16 : 0));
    }
}

internal sealed class SnowGolemAttackGoal(SnowGolem golem) : NavigationGoal(golem, 1.25f)
{
    private int cooldown;
    private int seenTicks;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => golem.AttackTarget is { } target && golem.IsValidTarget(target);
    public override ValueTask TickAsync()
    {
        var target = golem.AttackTarget!;
        var visible = golem.CanSee(target);
        seenTicks = visible ? seenTicks + 1 : 0;
        if ((target.Position - golem.Position).MagnitudeSquared() < 100 && seenTicks >= 20)
            Navigation.Stop();
        else
            MoveTo(target);
        golem.LookControl.LookAt(target);
        if (--cooldown <= 0 && visible)
        {
            cooldown = 40;
            var direction = target.Position + new VectorF(0, target.Dimension.Height * 0.85f, 0) - golem.EyePosition;
            direction.Y += MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z) * 0.2f;
            golem.Level.SpawnEntity(new MobProjectile(golem, EntityType.Snowball, golem.EyePosition, direction));
            golem.PlayMobSound("shoot");
        }
        return default;
    }
    public override void Start() { cooldown = 40; seenTicks = 0; }
}
