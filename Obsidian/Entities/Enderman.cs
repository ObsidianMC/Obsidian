using Obsidian.Entities.AI;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:enderman")]
public sealed partial class Enderman : PathfinderMob
{
    public IBlock? CarriedBlock { get; set; }
    internal long AngerTicks { get; set; }
    internal Guid AngryAt { get; set; }
    private Guid staringAt;
    private int stareTicks;
    private int chaseTicks;
    private bool staring;
    public Enderman() => Type = EntityType.Enderman;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override bool WaterSensitive => true;
    protected override string? SoundName => "enderman";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new EndermanStareGoal(this));
        actions.AddGoal(2, new MeleeAttackGoal(this));
        actions.AddGoal(7, new RandomStrollGoal(this, 1));
        actions.AddGoal(8, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(9, new RandomLookAroundGoal(this));
    }
    internal bool IsStaring(IPlayer player)
    {
        if (player.Inventory.GetItem(5)?.Type == Material.CarvedPumpkin || !IsValidTarget(player))
            return false;
        var direction = EyePosition - (player.Position + new VectorF(0, (float)(player.HeadY - player.Position.Y), 0));
        var distance = direction.Magnitude;
        if (distance < 0.001f)
            return false;
        direction /= distance;
        var look = player.GetLookDirection();
        return look.X * direction.X + look.Y * direction.Y + look.Z * direction.Z > 1 - 0.025f / distance && CanSee(player);
    }
    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi))
            return;
        if (InWater || Terrain.IsRainingAt((Vector)Position.Floor()))
            TryTeleport();
        if (AttackTarget is { } invalid && !IsValidTarget(invalid))
            AttackTarget = null;
        if (AngerTicks > 0 && --AngerTicks == 0)
        {
            AttackTarget = null;
            AngryAt = Guid.Empty;
            staringAt = Guid.Empty;
            SetAggressive(false);
        }
        if (AttackTarget == null && AngerTicks > 0)
            AttackTarget = Level.GetPlayersInRange(Position, FollowRange).FirstOrDefault(player => player.Uuid == AngryAt && IsValidTarget(player));
        if (AttackTarget == null)
        {
            var player = Level.GetPlayersInRange(Position, FollowRange).Where(IsStaring)
                .MinBy(player => (player.Position - Position).MagnitudeSquared());
            if (player == null)
            {
                stareTicks = 0;
                staringAt = Guid.Empty;
            }
            else
            {
                if (staringAt != player.Uuid) { staringAt = player.Uuid; stareTicks = 0; }
                LookControl.LookAt(player);
                if (++stareTicks >= 5)
                {
                    AttackTarget = player;
                    AngryAt = player.Uuid;
                    AngerTicks = Random.Next(400, 800);
                    SetAggressive(true);
                    PlayMobSound("stare");
                }
            }
        }
        var nowStaring = AttackTarget is IPlayer target && IsStaring(target);
        if (nowStaring != staring) { staring = nowStaring; SynchronizeMetadata(); }
        if (AttackTarget is { } enemy)
        {
            if (staring && (enemy.Position - Position).MagnitudeSquared() < 16)
                TryTeleport();
            else if (!staring && (enemy.Position - Position).MagnitudeSquared() > 256 && ++chaseTicks >= 30)
            {
                chaseTicks = 0;
                TryTeleport(enemy.Position);
            }
        }
        if (CarriedBlock == null && Random.Next(20) == 0)
        {
            var point = (Vector)(Position + new VectorF(Random.Next(-2, 3), Random.Next(2), Random.Next(-2, 3))).Floor();
            var block = Terrain.GetBlock(point);
            if (block != null && TagsRegistry.Block.EndermanHoldable.Entries.Contains(block.RegistryId) &&
                Terrain.HasLineOfSight(EyePosition, (VectorF)point + 0.5f))
            {
                CarriedBlock = block;
                await Level.SetBlockAsync(point, BlocksRegistry.Air, true);
                SynchronizeMetadata();
            }
        }
        else if (CarriedBlock is { } carried && Random.Next(2000) == 0)
        {
            var point = (Vector)(Position + new VectorF(Random.Next(-1, 2), Random.Next(2), Random.Next(-1, 2))).Floor();
            if (Terrain.GetBlock(point)?.IsAir == true && Terrain.GetBlock(new Vector(point.X, point.Y - 1, point.Z)) is { } floor &&
                BlockCollisionShapes.Get(floor).Count > 0 && !Level.GetEntitiesInRange((VectorF)point + 0.5f, 2)
                    .Any(entity => MobTerrain.Overlaps(entity.Dimension.CreateBBFromPosition(entity.Position),
                        new BoundingBox((VectorF)point, (VectorF)point + 1))))
            {
                await Level.SetBlockAsync(point, carried, true);
                CarriedBlock = null;
                SynchronizeMetadata();
            }
        }
    }
    internal bool TryAvoidProjectile()
    {
        for (var attempt = 0; attempt < 64; attempt++)
            if (TryTeleport()) return true;
        return false;
    }
    internal bool TryTeleport(VectorF? towards = null)
    {
        if (Level is not AbstractLevel level || !Alive)
            return false;
        var destination = towards is { } target ? target + new VectorF(Random.Next(-8, 9), Random.Next(-8, 9), Random.Next(-8, 9)) :
            Position + new VectorF(Random.Next(-32, 33), Random.Next(-32, 33), Random.Next(-32, 33));
        var point = (Vector)destination.Floor();
        if (point.Y < level.MinY || point.Y >= level.MinY + level.Height)
            return false;
        while (point.Y > level.MinY && Terrain.GetBlock(new Vector(point.X, point.Y - 1, point.Z)) is { } floor &&
            BlockCollisionShapes.Get(floor).Count == 0 && !floor.IsLiquid)
            point.Y--;
        var support = Terrain.GetBlock(new Vector(point.X, point.Y - 1, point.Z));
        destination = new VectorF(point.X + 0.5f, point.Y, point.Z + 0.5f);
        if (support == null || support.IsLiquid || BlockCollisionShapes.Get(support).Count == 0 ||
            Terrain.GetBlock(point) is not { IsLiquid: false } ||
            Enumerable.Range(0, (int)MathF.Ceiling(Dimension.Height)).Any(height =>
                Terrain.GetBlock(new Vector(point.X, point.Y + height, point.Z)) is not { IsLiquid: false }) || !Terrain.IsFree(Dimension.CreateBBFromPosition(destination)) ||
            !level.TryMoveEntity(this, Position, destination))
            return false;
        PlayMobSound("teleport");
        CompleteTeleport(destination);
        PlayMobSound("teleport");
        return true;
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (ReferenceEquals(source, this))
            TryTeleport();
        else if (IsValidTarget(source))
        {
            AttackTarget = source;
            AngryAt = source.Uuid;
            AngerTicks = Random.Next(400, 800);
            SetAggressive(true);
        }
        return default;
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.EnderPearl, Random.Next(2));
        if (CarriedBlock is { } block) DropItem(block.Material, 1);
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.OptionalBlockState);
        writer.WriteVarInt(CarriedBlock == null ? 0 : CarriedBlock.GetHashCode() + 1);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean);
        writer.WriteBoolean(AngerTicks > 0);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean);
        writer.WriteBoolean(staring);
    }
}

internal sealed class EndermanStareGoal(Enderman enderman) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Jump;
    public override bool CanUse() => enderman.AttackTarget is IPlayer player && enderman.IsStaring(player);
    public override void Start() { ((Navigator)enderman.Navigator!).Stop(); enderman.MoveControl.Stop(); }
}
