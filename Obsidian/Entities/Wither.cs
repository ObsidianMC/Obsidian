using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:wither")]
public sealed partial class Wither : BossMob
{
    private const int SpawnCharge = 220;
    private readonly int[] headTargets = new int[3];
    private readonly long[] nextShot = new long[3];
    private readonly int[] idleShots = new int[2];
    private int invulnerableTicks;
    private int breakBlocksTicks;
    public Wither() => Type = EntityType.Wither;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    internal override bool FlyingNavigation => true;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "wither";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 50;
    internal bool Armored => Health <= GetAttributeValue("minecraft:generic.max_health") / 2;
    protected override float BarProgress => invulnerableTicks > 0 ? 1 - (float)invulnerableTicks / SpawnCharge : base.BarProgress;
    protected override VectorD Travel() => VolumeMovement.Travel(this, true);
    protected override ValueTask TickAirSupplyAsync() { Air = 300; return default; }

    internal void BeginSpawnCharge()
    {
        invulnerableTicks = SpawnCharge;
        Health = GetAttributeValue("minecraft:generic.max_health") / 3;
    }

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new WitherFlightGoal(this));
        actions.AddGoal(5, new RandomStrollGoal(this, 1));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, CanAttack));
    }

    private bool CanAttack(IEntity target) => target is Living && IsValidTarget(target) &&
        !TagsRegistry.EntityType.WitherFriends.Entries.Contains((int)target.Type);

    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (invulnerableTicks > 0)
        {
            if (--invulnerableTicks == 0 && Level is AbstractLevel level)
            {
                await level.ExplodeAsync(this, 7);
                PacketBroadcaster.QueuePacketToLevel(Level, new LevelEventPacket(1023, (Vector)Position.Floor(), 0, true));
            }
            if (AiTick % 10 == 0) Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 10);
            SynchronizeMetadata();
            return;
        }
        if (AiTick % 20 == 0) Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 1);
        if (AttackTarget != null && (!CanAttack(AttackTarget) || !CanSee(AttackTarget))) AttackTarget = null;
        SetHeadTarget(0, AttackTarget?.EntityId ?? 0);
        if (AttackTarget is { } main && AiTick >= nextShot[0])
        {
            Shoot(0, main.Position + new VectorD(0, main.Dimension.Height / 2, 0), Random.NextSingle() < 0.001f);
            nextShot[0] = AiTick + 40;
        }
        for (var head = 1; head < 3; head++)
        {
            if (AiTick < nextShot[head]) continue;
            nextShot[head] = AiTick + Random.Next(10, 20);
            if (Level.LevelData.Difficulty is Difficulty.Normal or Difficulty.Hard && ++idleShots[head - 1] > 15)
            {
                Shoot(head, Position + new VectorD(Random.Next(-10, 11), Random.Next(-5, 6), Random.Next(-10, 11)), true);
                idleShots[head - 1] = 0;
            }
            var target = Level.GetEntitiesInRange(Position, 30).FirstOrDefault(entity => entity.EntityId == headTargets[head]);
            if (target != null && CanAttack(target) && CanSee(target))
            {
                Shoot(head, target.Position + new VectorD(0, target.Dimension.Height / 2, 0), false);
                nextShot[head] = AiTick + Random.Next(40, 60);
                idleShots[head - 1] = 0;
            }
            else
            {
                var choices = Level.GetEntitiesInRange(Position, 20).Where(entity => CanAttack(entity) && CanSee(entity)).ToArray();
                SetHeadTarget(head, choices.Length == 0 ? 0 : choices[Random.Next(choices.Length)].EntityId);
            }
        }
        if (breakBlocksTicks > 0 && --breakBlocksTicks == 0) await BreakBlocksAsync();
    }

    private void SetHeadTarget(int head, int id)
    {
        if (headTargets[head] == id) return;
        headTargets[head] = id;
        SynchronizeMetadata();
    }

    private void Shoot(int head, VectorD target, bool dangerous)
    {
        var angle = (Yaw.Degrees + (head == 1 ? 0 : 180)) * Math.PI / 180;
        var origin = head == 0 ? Position + new VectorD(0, 3, 0) :
            Position + new VectorD(Math.Cos(angle) * 1.3, 2.2, Math.Sin(angle) * 1.3);
        Level.SpawnEntity(new WitherSkull(this, origin, target - origin, dangerous));
        PlayMobSound("shoot");
    }

    private async ValueTask BreakBlocksAsync()
    {
        if (!Level.LevelData.GetBooleanRule("mob_griefing")) return;
        var position = (Vector)Position.Floor();
        var broken = false;
        for (var x = -1; x <= 1; x++)
        for (var z = -1; z <= 1; z++)
        for (var y = 0; y <= 3; y++)
        {
            var point = position + new Vector(x, y, z);
            var block = Terrain.GetBlock(point);
            if (block == null || block.IsAir || TagsRegistry.Block.WitherImmune.Entries.Contains(block.RegistryId)) continue;
            await Level.SetBlockAsync(point, BlocksRegistry.Air);
            broken = true;
        }
        if (broken) PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new LevelEventPacket(1022, position, 0));
    }

    protected override bool CanTakeDamage(IEntity source) => invulnerableTicks == 0 && source.Type != EntityType.Wither &&
        !TagsRegistry.EntityType.WitherFriends.Entries.Contains((int)source.Type);

    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (breakBlocksTicks == 0) breakBlocksTicks = 20;
        for (var head = 0; head < 2; head++) idleShots[head] += 3;
        return default;
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (Level.LevelData.GetBooleanRule("mob_drops"))
        {
            var star = new ItemEntity { Level = Level, EntityId = Server.GetNextEntityId(), Position = Position,
                Item = ItemsRegistry.GetSingleItem(Material.NetherStar) };
            star.SetExtendedLifetime();
            Level.SpawnEntity(star);
        }
        return default;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        for (byte head = 0; head < 3; head++)
        { writer.WriteEntityMetadataType((byte)(16 + head), EntityMetadataType.VarInt); writer.WriteVarInt(headTargets[head]); }
        writer.WriteEntityMetadataType(19, EntityMetadataType.VarInt); writer.WriteVarInt(invulnerableTicks);
    }

    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("Invul", invulnerableTicks);
        writer.WriteInt("ObsidianWitherBreakTicks", breakBlocksTicks);
        writer.WriteArray("ObsidianWitherHeadTimers", nextShot.Select(tick => (int)Math.Clamp(tick - AiTick, 0, int.MaxValue)).ToArray());
        writer.WriteArray("ObsidianWitherIdleShots", idleShots);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        invulnerableTicks = Math.Clamp(tag.TryGetTagValue<int>("Invul", out var ticks) ? ticks : 0, 0, SpawnCharge);
        breakBlocksTicks = Math.Clamp(tag.GetInt("ObsidianWitherBreakTicks"), 0, 20);
        if (tag.TryGetTag<NbtArray<int>>("ObsidianWitherHeadTimers", out var timers))
            for (var index = 0; index < Math.Min(nextShot.Length, timers.Count); index++) nextShot[index] = Math.Max(0, timers[index]);
        if (tag.TryGetTag<NbtArray<int>>("ObsidianWitherIdleShots", out var shots))
            for (var index = 0; index < Math.Min(idleShots.Length, shots.Count); index++) idleShots[index] = Math.Max(0, shots[index]);
    }

    private sealed class WitherFlightGoal(Wither mob) : Goal
    {
        public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
        public override bool CanUse() => mob.invulnerableTicks > 0 || mob.AttackTarget != null;
        public override bool RequiresUpdateEveryTick => true;
        public override ValueTask TickAsync()
        {
            if (mob.invulnerableTicks > 0) { mob.MoveControl.Stop(); return default; }
            if (mob.AttackTarget is { } target)
            {
                var delta = target.Position - mob.Position;
                var height = target.Position.Y + (mob.Armored ? 0.5 : 5);
                var horizontal = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
                var destination = horizontal > 9 ? new VectorD(target.Position.X, height, target.Position.Z) :
                    new VectorD(mob.Position.X, height, mob.Position.Z);
                mob.MoveControl.MoveTo(destination, 2);
                mob.LookControl.LookAt(target);
            }
            return default;
        }
    }
}
