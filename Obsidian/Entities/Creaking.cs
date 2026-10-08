using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:creaking")]
public sealed partial class Creaking : PathfinderMob
{
    public bool CanMove { get; private set; } = true;
    public bool Active { get; private set; }
    public bool TearingDown { get; private set; }
    public Vector? HomePosition { get; set; }
    private int teardownTicks;
    public Creaking() { Type = EntityType.Creaking; IsFireImmune = true; }
    protected override bool UsesAi => true;
    protected override bool CanDespawn => HomePosition == null;
    internal override bool Hostile => true;
    internal override bool PanicsWhenHurt => false;
    protected override string? SoundName => "creaking";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new MeleeAttackGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 0.3f));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, entity => Active && entity is IPlayer));
    }
    protected override bool CanTakeDamage(IEntity source)
    {
        if (HomePosition == null) return true;
        if (source is IPlayer { GameMode: GameMode.Creative }) return true;
        if (!TearingDown && !ReferenceEquals(source, this)) { SendEntityEvent(66); PlayMobSound("sway"); }
        return false;
    }
    protected override async ValueTask TickMobAsync()
    {
        if (Level.LevelData.Difficulty == Difficulty.Peaceful) { await RemoveAsync(); return; }
        if (HomePosition is { } home && Terrain.GetBlock(home)?.Material != Material.CreakingHeart) TearingDown = true;
        if (TearingDown)
        {
            if (teardownTicks++ == 0) { SendEntityEvent(61); SynchronizeMetadata(); }
            if (teardownTicks >= 45) await RemoveAsync();
            return;
        }
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        var oldActive = Active;
        var oldCanMove = CanMove;
        var players = Level.GetPlayersInRange(Position, 32).Where(player => IsValidTarget(player) && CanSee(player)).ToArray();
        if (players.Length == 0) Active = false;
        var observed = false;
        foreach (var player in players)
        {
            var eye = player.Position + new VectorD(0, player.Dimension.Height * 0.85f, 0);
            var look = (VectorD)player.GetLookDirection();
            for (var height = 0.5f; height <= 1; height += 0.25f)
            {
                var direction = Position + new VectorD(0, Dimension.Height * height, 0) - eye;
                var distance = direction.Magnitude;
                if (distance < 0.001f || (look.X * direction.X + look.Y * direction.Y + look.Z * direction.Z) / distance > 0.5f)
                { observed = true; break; }
            }
            if (observed && !Active && IsInRange(player, 12)) { Active = true; AttackTarget = player; PlayMobSound("activate"); }
            if (observed && Active) break;
        }
        CanMove = !Active || !observed;
        if (!CanMove)
        {
            GoalController?.Pause();
            (Navigator as Navigator)?.Stop();
            MoveControl.Stop();
        }
        else if (GoalController is GoalSelector { IsPaused: true } goals) goals.Resume();
        if (oldActive && !Active) { AttackTarget = null; PlayMobSound("deactivate"); }
        if (oldActive != Active || oldCanMove != CanMove) SynchronizeMetadata();
        if (HomePosition is { } position && (Position - (VectorD)position).MagnitudeSquared() > 1024)
            MoveControl.MoveTo((VectorD)position, 1);
    }
    protected override VectorD Travel()
    {
        if (!CanMove || TearingDown) { MoveControl.Stop(); Motion = new VectorD(0, Motion.Y, 0); }
        return base.Travel();
    }
    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        if (!CanMove || !Active || TearingDown) return;
        SendEntityEvent(4);
        PlayMobSound("attack");
        await base.PerformMeleeAttackAsync(target);
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean); writer.WriteBoolean(CanMove);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean); writer.WriteBoolean(Active);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean); writer.WriteBoolean(TearingDown);
        writer.WriteEntityMetadataType(19, EntityMetadataType.OptionalBlockPos); writer.WriteOptional(HomePosition);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        if (HomePosition is { } home) writer.WriteArray("home_pos", new[] { home.X, home.Y, home.Z });
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        if (tag.TryGetTag<NbtArray<int>>("home_pos", out var home) && home.Count == 3)
        { var values = home.GetArray(); HomePosition = new Vector(values[0], values[1], values[2]); }
    }
}
