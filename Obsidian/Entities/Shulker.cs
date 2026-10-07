using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:shulker")]
public sealed partial class Shulker : PathfinderMob
{
    private static readonly Vector[] directions = [new(0, -1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1), new(-1, 0, 0), new(1, 0, 0)];
    private byte face;
    private byte peek;
    private long nextShot;
    private long closeAt;
    public byte Color { get; set; } = 16;
    internal bool Closed => peek == 0;
    public Shulker() { Type = EntityType.Shulker; NoGravity = true; }
    protected override bool UsesAi => true;
    internal override bool Hostile => false;
    protected override bool CanDespawn => false;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "shulker";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    protected override VectorD Travel() { Motion = VectorD.Zero; return Position; }
    protected override void FinalizeSpawn() { PersistenceRequired = true; UpdateArmor(); }
    private void UpdateArmor() => TryUpdateAttribute("minecraft:generic.armor", Closed ? 20 : 0);
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(2, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => Level.LevelData.Difficulty != Difficulty.Peaceful && target is IPlayer));
    }
    private void SetPeek(byte value)
    {
        if (peek == value) return;
        peek = value;
        UpdateArmor();
        PlayMobSound(Closed ? "close" : "open");
        SynchronizeMetadata();
    }
    protected override ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return default;
        var point = (Vector)Position.Floor();
        if (AiTick % 20 == 1 && !CanAttach(point, face))
        {
            var attachment = FindAttachment(point);
            if (attachment >= 0) { face = (byte)attachment; SynchronizeMetadata(); }
            else Teleport();
        }
        if (Level.LevelData.Difficulty == Difficulty.Peaceful) AttackTarget = null;
        if (AttackTarget is { } target && IsValidTarget(target) && IsInRange(target, 20))
        {
            LookControl.LookAt(target);
            if (Closed) { SetPeek(100); nextShot = AiTick + 20; }
            if (AiTick >= nextShot && CanSee(target))
            {
                nextShot = AiTick + 20 + Random.Next(10) * 10;
                Level.SpawnEntity(new ShulkerBullet { Level = Level, EntityId = Server.GetNextEntityId(),
                    Position = Position + new VectorD(0, 0.5, 0), Owner = this, TargetUuid = target.Uuid });
                PlayMobSound("shoot");
            }
        }
        else if (peek == 100 || peek > 0 && AiTick >= closeAt) SetPeek(0);
        else if (Closed && Random.Next(40) == 0) { SetPeek(30); closeAt = AiTick + Random.Next(20, 60); }
        return default;
    }
    private bool CanAttach(Vector point, int direction)
    {
        var adjacent = Terrain.GetBlock(point + directions[direction]);
        return adjacent != null && !adjacent.IsLiquid && BlockCollisionShapes.Get(adjacent).Any(box => box.Min == VectorD.Zero && box.Max == new VectorD(1));
    }
    private int FindAttachment(Vector point)
    {
        for (var direction = 0; direction < directions.Length; direction++) if (CanAttach(point, direction)) return direction;
        return -1;
    }
    private void Teleport()
    {
        if (Level is not AbstractLevel level) return;
        var origin = (Vector)Position.Floor();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var point = origin + new Vector(Random.Next(-8, 9), Random.Next(-8, 9), Random.Next(-8, 9));
            var destination = new VectorD(point.X + 0.5, point.Y, point.Z + 0.5);
            if (!Terrain.IsFree(Dimension.CreateBBFromPosition(destination))) continue;
            var attachment = FindAttachment(point);
            if (attachment < 0 || !level.TryMoveEntity(this, Position, destination)) continue;
            PlayMobSound("teleport");
            Position = destination;
            BoundingBox = Dimension.CreateBBFromPosition(Position);
            face = (byte)attachment;
            AttackTarget = null;
            SetPeek(0);
            SynchronizeMetadata();
            return;
        }
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (Health < GetAttributeValue("minecraft:generic.max_health") / 2 && Random.Next(4) == 0) Teleport();
        return default;
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (Random.Next(2) == 0) DropItem(Material.ShulkerShell);
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Direction); writer.WriteVarInt(face);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte); writer.WriteByte(peek);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Byte); writer.WriteByte(Color);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteByte("AttachFace", face);
        writer.WriteByte("Peek", peek);
        writer.WriteByte("Color", Color);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        face = tag.TryGetTagValue<byte>("AttachFace", out var attachment) ? (byte)Math.Clamp((int)attachment, 0, 5) : (byte)0;
        peek = tag.TryGetTagValue<byte>("Peek", out var savedPeek) ? (byte)Math.Clamp((int)savedPeek, 0, 100) : (byte)0;
        Color = tag.TryGetTagValue<byte>("Color", out var color) ? (byte)Math.Clamp((int)color, 0, 16) : (byte)16;
        closeAt = AiTick + 40;
        UpdateArmor();
    }
}

[MinecraftEntity("minecraft:shulker_bullet")]
public sealed partial class ShulkerBullet : Arrow
{
    internal Guid TargetUuid { get; set; }
    public ShulkerBullet()
    {
        Type = EntityType.ShulkerBullet;
        NoGravity = true;
        Effect = (int)PotionEffect.Levitation - 1;
        EffectDuration = 200;
    }
    public override async ValueTask TickAsync()
    {
        if (Level.LevelData.Difficulty == Difficulty.Peaceful) { await RemoveAsync(); return; }
        var target = Level.GetEntitiesInRange(Position, 64).FirstOrDefault(entity => entity.Uuid == TargetUuid && entity.Health > 0);
        if (target != null && target is not IPlayer { GameMode: GameMode.Spectator })
        {
            var delta = target.Position + new VectorD(0, target.Dimension.Height / 2, 0) - Position;
            if (delta.Magnitude > 0.0001) Motion += delta / delta.Magnitude * 0.06;
            if (Motion.Magnitude > 0.5) Motion = Motion / Motion.Magnitude * 0.5;
        }
        else { NoGravity = false; }
        await base.TickAsync();
    }
    public override ValueTask DamageAsync(IEntity source, float amount = 1) => amount > 0 && source.Level == Level ? RemoveAsync() : default;
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        if (TargetUuid != Guid.Empty) tag.Set(new NbtArray<int>("Target", EntityNbt.UuidToInts(TargetUuid)));
    }
    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        if (tag.TryGetTag<NbtArray<int>>("Target", out var target) && target.Count == 4) TargetUuid = EntityNbt.UuidFromInts(target.GetArray());
    }
}
