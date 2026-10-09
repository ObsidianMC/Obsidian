using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:phantom")]
public sealed partial class Phantom : PathfinderMob
{
    private int size;
    internal Vector Anchor { get; set; }
    public Phantom() => Type = EntityType.Phantom;
    public int Size
    {
        get => size;
        set { size = Math.Clamp(value, 0, 64); TryUpdateAttribute("minecraft:generic.attack_damage", 6 + size); }
    }
    protected override float DimensionScale => 1 + 0.15f * Size;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    internal override bool FlyingNavigation => true;
    internal override float MovementSpeed => 1;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "phantom";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    protected override VectorD Travel() => VolumeMovement.Travel(this, true);
    protected override void FinalizeSpawn() { Size = 0; Anchor = (Vector)Position.Floor() + new Vector(0, 5, 0); }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new PhantomFlightGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, target => target is IPlayer && Math.Abs(target.Position.Y - Position.Y) <= 64));
    }
    protected override ValueTask TickMobAsync()
    {
        if (Level.DimensionName == "minecraft:overworld" && Level.DayTime is >= 0 and < 12000 &&
            !Level.LevelData.Raining && !InWater && Terrain.GetSkyLight((Vector)EyePosition.Floor()) == 15)
            Ignite(8);
        return default;
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (source is IPlayer) DropItem(Material.PhantomMembrane, Random.Next(2));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.VarInt);
        writer.WriteVarInt(Size);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("size", Size);
        writer.WriteArray("anchor_pos", new[] { Anchor.X, Anchor.Y, Anchor.Z });
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        Size = tag.TryGetTagValue<int>("size", out var saved) ? saved : 0;
        if (tag.TryGetTag<NbtArray<int>>("anchor_pos", out var anchor) && anchor.Count == 3)
        {
            var coordinates = anchor.GetArray();
            Anchor = new Vector(coordinates[0], coordinates[1], coordinates[2]);
        }
        else Anchor = (Vector)Position.Floor() + new Vector(0, 5, 0);
    }
}

internal sealed class PhantomFlightGoal(Phantom phantom) : Goal
{
    private bool swooping;
    private long nextSwoop;
    private float angle;
    private float radius;
    private float height;
    private int direction;
    private int swoopTicks;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => true;
    public override void Start()
    {
        nextSwoop = phantom.AiTick + 200;
        radius = phantom.Random.Next(5, 16);
        height = phantom.Random.Next(-4, 6);
        direction = phantom.Random.Next(2) == 0 ? -1 : 1;
        angle = phantom.Random.NextSingle() * MathF.PI * 2;
    }
    public override async ValueTask TickAsync()
    {
        var target = phantom.AttackTarget;
        if (target == null || !phantom.IsValidTarget(target)) swooping = false;
        if (phantom.AiTick % 20 == 0 && phantom.GetEntitiesNear(16).Any(entity => entity.Type == EntityType.Cat))
        {
            swooping = false;
            nextSwoop = phantom.AiTick + 200;
        }
        if (!swooping && target != null && phantom.AiTick >= nextSwoop)
        {
            swooping = true;
            swoopTicks = 200;
            phantom.PlayMobSound("swoop");
        }
        if (swooping && target != null)
        {
            phantom.LookControl.LookAt(target);
            phantom.MoveControl.MoveTo(target.Position + new VectorD(0, target.Dimension.Height / 2, 0), 2);
            if (phantom.IsWithinMeleeRange(target))
            {
                await phantom.PerformMeleeAttackAsync(target);
                swoopTicks = 0;
            }
            if (--swoopTicks <= 0 || phantom.AiTick - phantom.LastHurtTick < 20)
            {
                swooping = false;
                nextSwoop = phantom.AiTick + phantom.Random.Next(160, 241);
                phantom.Anchor = (Vector)target.Position.Floor() + new Vector(0, phantom.Random.Next(20, 40), 0);
            }
            return;
        }
        angle += direction * 0.03f;
        var destination = new VectorD(phantom.Anchor.X + Math.Cos(angle) * radius, phantom.Anchor.Y + height,
            phantom.Anchor.Z + Math.Sin(angle) * radius);
        if (!phantom.Terrain.IsFree(phantom.Dimension.CreateBBFromPosition(destination)))
        {
            height += 1;
            direction = -direction;
        }
        phantom.MoveControl.MoveTo(destination, 1);
        phantom.LookControl.LookAt(destination);
    }
    public override void Stop() => phantom.MoveControl.Stop();
}
