using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:tadpole")]
public sealed partial class Tadpole : PathfinderMob, IAgeLockable
{
    internal const int MaturationTicks = 24000;
    public Tadpole() => Type = EntityType.Tadpole;
    public int Age { get; internal set; }

    /// <summary>Whether a golden dandelion paused the tadpole's growing into a frog (vanilla's <c>AgeLocked</c>).</summary>
    public bool AgeLocked { get; internal set; }

    bool IAgeLockable.CanLockAge => true;

    int IAgeLockable.AgeLockTimer { get; set; }

    // Vanilla's Tadpole.setAgeLockedData: the lock toggles and the tadpole starts growing from the start again.
    void IAgeLockable.ToggleAgeLock()
    {
        AgeLocked = !AgeLocked;
        Age = 0;
        ((IAgeLockable)this).AgeLockTimer = AgeLocks.ParticleTicks;
        SynchronizeMetadata();
    }

    public bool FromBucket { get; internal set; }
    protected override bool UsesAi => true;
    internal override bool SwimmingNavigation => true;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "tadpole";

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new PanicGoal(this, 1.25f));
        actions.AddGoal(2, new SeekWaterGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 1));
    }

    protected override VectorD Travel() => VolumeMovement.Travel(this, InWater);

    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi))
            return;

        AgeLocks.Tick(this, this);
        if (AgeLocked)
            return;

        if (++Age >= MaturationTicks)
        {
            var frog = (Frog)await ConvertToAsync(EntityType.Frog);
            frog.Health = frog.GetAttributeValue("minecraft:generic.max_health");
            frog.SelectVariant();
            frog.SynchronizeMetadata();
        }
        else if (!InWater && MovementFlags.HasFlag(MovementFlags.OnGround))
        {
            Motion += new VectorD((Random.NextSingle() - 0.5f) * 0.2f, 0.4f, (Random.NextSingle() - 0.5f) * 0.2f);
        }
    }

    protected override async ValueTask TickAirSupplyAsync()
    {
        if (InWater)
        {
            Air = 300;
        }
        else if (--Air <= -20)
        {
            Air = 0;
            await DamageEnvironmentAsync(2);
    }
    }

    internal override async ValueTask<bool> InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (await TryCaptureInBucketAsync(player, hand, ItemsRegistry.TadpoleBucket))
            return true;
        if (!Alive || player.Level != Level || player.GameMode == GameMode.Spectator || !IsInRange(player, 4))
            return false;
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        // Vanilla's Tadpole.mobInteract: a locked tadpole doesn't eat.
        if (item is { Count: > 0, Holder.Id: ItemIds.SlimeBall } && !AgeLocked)
        {
            Age += Math.Max(1, (MaturationTicks - Age) / 10);
            await ConsumeInteractionItemAsync(player, hand);
            SendEntityEvent(18);
            return true;
        }

        return false;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(FromBucket);

        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean);
        writer.WriteBoolean(AgeLocked);
    }

    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("Age", Age);
        writer.WriteBool("FromBucket", FromBucket);
        writer.WriteBool("AgeLocked", AgeLocked);
    }

    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        Age = Math.Clamp((tag.TryGetNumber<int>("Age", out var savedAge) ? savedAge : 0), 0, MaturationTicks);
        FromBucket = tag.TryGetBool("FromBucket", out var bucket) && bucket;
        AgeLocked = tag.TryGetBool("AgeLocked", out var locked) && locked;
        PersistenceRequired |= FromBucket;
    }
}

internal sealed class SeekWaterGoal(PathfinderMob mob, bool onlyDuringDay = false) : NavigationGoal(mob, 1)
{
    private VectorD destination;

    public override bool CanUse()
    {
        if (mob.InWater || onlyDuringDay && (mob.Level.DayTime is < 0 or >= 12000) || mob.Random.Next(20) != 0)
            return false;
        var origin = (Vector)mob.Position.Floor();
        for (var y = -2; y <= 2; y++)
        {
            for (var x = -8; x <= 8; x++)
            {
                for (var z = -8; z <= 8; z++)
                {
                    var point = new Vector(origin.X + x, origin.Y + y, origin.Z + z);
                    if (mob.Terrain.GetBlock(point)?.RegistryId == BlockIds.Water &&
                        mob.Terrain.IsFree(mob.Dimension.CreateBBFromPosition((VectorD)point + new VectorD(0.5f, 0, 0.5f))))
                    {
                        destination = (VectorD)point + new VectorD(0.5f, 0, 0.5f);
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public override void Start() => MoveTo(destination);
    public override bool CanContinue() => !mob.InWater && Navigation.IsNavigating;
}
