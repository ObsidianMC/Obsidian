using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:evoker")]
public sealed partial class Evoker : PathfinderMob
{
    private int spellTicks;
    private byte spell;
    private int warmup;
    private long nextFangs;
    private long nextSummon;
    private long nextWololo;
    private Sheep? sheep;
    public Evoker() => Type = EntityType.Evoker;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override string? SoundName => "evoker";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 10;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new EvokerCastingGoal(this));
        actions.AddGoal(2, new AvoidEntityGoal(this, target => target is IPlayer, 8, 1));
        actions.AddGoal(8, new RandomStrollGoal(this, 0.6f));
        actions.AddGoal(9, new LookAtPlayerGoal(this, 3));
        actions.AddGoal(10, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => target is IPlayer));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, target => target.Type is EntityType.Villager or EntityType.IronGolem or EntityType.WanderingTrader));
    }
    internal bool Casting => spellTicks > 0;
    protected override ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return default;
        if (spellTicks > 0)
        {
            if (warmup > 0 && --warmup == 0)
            {
                if (spell == 1 && AttackTarget is { } target && IsValidTarget(target)) CastFangs(target);
                else if (spell == 2) SummonVexes();
                else if (spell == 3 && sheep is { Alive: true, IsRemoved: false, Color: 11 } && sheep.Level == Level)
                { sheep.Color = 14; sheep.SynchronizeMetadata(); }
                PlayMobSound("cast_spell");
            }
            if (--spellTicks == 0) { spell = 0; sheep = null; SynchronizeMetadata(); }
            return default;
        }
        if (AttackTarget is { } enemy && IsValidTarget(enemy))
        {
            if (AiTick >= nextSummon && Random.Next(1, 9) > GetEntitiesNear(16).OfType<Vex>().Count())
            {
                nextSummon = AiTick + 340;
                BeginSpell(2, 100, 20, "prepare_summon");
            }
            else if (AiTick >= nextFangs)
            {
                nextFangs = AiTick + 100;
                BeginSpell(1, 40, 20, "prepare_attack");
            }
        }
        else if (AiTick >= nextWololo)
        {
            nextWololo = AiTick + 140;
            var candidates = GetEntitiesNear(16).OfType<Sheep>().Where(candidate => candidate.Color == 11 && Math.Abs(candidate.Position.Y - Position.Y) <= 4).ToArray();
            if (candidates.Length > 0)
            {
                sheep = candidates[Random.Next(candidates.Length)];
                BeginSpell(3, 60, 40, "prepare_wololo");
            }
        }
        return default;
    }
    private void BeginSpell(byte kind, int duration, int delay, string sound)
    {
        spell = kind;
        spellTicks = duration;
        warmup = delay;
        SynchronizeMetadata();
        PlayMobSound(sound);
    }
    private void SummonVexes()
    {
        for (var i = 0; i < 3; i++)
        {
            var point = (Vector)Position.Floor() + new Vector(Random.Next(-2, 3), 1, Random.Next(-2, 3));
            var vex = (Vex)Level.GetNewEntitySpawner().WithEntityType(EntityType.Vex).AtPosition(new VectorD(point.X + 0.5, point.Y, point.Z + 0.5)).Spawn();
            vex.OwnerUuid = Uuid;
            vex.BoundPosition = point;
            vex.LifeTicks = 20 * Random.Next(30, 120);
            vex.AttackTarget = AttackTarget;
        }
    }
    private void CastFangs(IEntity target)
    {
        var angle = Math.Atan2(target.Position.Z - Position.Z, target.Position.X - Position.X);
        var minY = Math.Min(Position.Y, target.Position.Y);
        var maxY = Math.Max(Position.Y, target.Position.Y) + 1;
        if ((target.Position - Position).MagnitudeSquared() < 9)
        {
            for (var i = 0; i < 5; i++) SpawnFang(Position.X + Math.Cos(angle + i * Math.PI * 0.4) * 1.5, Position.Z + Math.Sin(angle + i * Math.PI * 0.4) * 1.5, minY, maxY, angle + i * Math.PI * 0.4, 0);
            for (var i = 0; i < 8; i++) SpawnFang(Position.X + Math.Cos(angle + i * Math.PI * 0.25 + 1.256637) * 2.5, Position.Z + Math.Sin(angle + i * Math.PI * 0.25 + 1.256637) * 2.5, minY, maxY, angle + i * Math.PI * 0.25 + 1.256637, 3);
        }
        else
            for (var i = 0; i < 16; i++) SpawnFang(Position.X + Math.Cos(angle) * 1.25 * (i + 1), Position.Z + Math.Sin(angle) * 1.25 * (i + 1), minY, maxY, angle, i);
    }
    private void SpawnFang(double x, double z, double minY, double maxY, double angle, int delay)
    {
        for (var y = (int)Math.Floor(maxY); y >= (int)Math.Floor(minY) - 1; y--)
        {
            var ground = Terrain.GetBlock(new Vector((int)Math.Floor(x), y - 1, (int)Math.Floor(z)));
            if (ground == null || ground.IsLiquid || !BlockCollisionShapes.Get(ground).Any(box => box.Max.Y >= 1)) continue;
            var block = Terrain.GetBlock(new Vector((int)Math.Floor(x), y, (int)Math.Floor(z)));
            if (block == null) return;
            var shapes = BlockCollisionShapes.Get(block);
            var height = shapes.Count == 0 ? 0 : shapes.Max(box => box.Max.Y);
            Level.SpawnEntity(new EvokerFangs { Level = Level, EntityId = Server.GetNextEntityId(), Position = new VectorD(x, y + height, z),
                Yaw = (float)(angle * 180 / Math.PI), Warmup = delay, OwnerUuid = Uuid });
            return;
        }
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.TotemOfUndying);
        if (source is IPlayer) DropItem(Material.Emerald, Random.Next(2));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(false);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        writer.WriteByte(spell);
    }
    protected override void WriteAdditionalSave(INbtWriter writer) => writer.WriteInt("SpellTicks", spellTicks);
    protected override void ReadAdditionalSave(NbtCompound tag) => spellTicks = Math.Clamp(tag.TryGetTagValue<int>("SpellTicks", out var ticks) ? ticks : 0, 0, 100);
}

internal sealed class EvokerCastingGoal(Evoker evoker) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => evoker.Casting;
    public override void Start() { ((Navigator)evoker.Navigator!).Stop(); evoker.MoveControl.Stop(); }
    public override ValueTask TickAsync() { if (evoker.AttackTarget is { } target) evoker.LookControl.LookAt(target); return default; }
}
