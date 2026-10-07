using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using System.Threading;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:camel")]
public partial class Camel : AbstractHorse
{
    internal const int DashCooldownTicks = 55;
    private int pendingDashCharge = -1;
    private int dashTicks;
    internal int DashCooldown { get; set; }
    internal long LastPoseChangeTick { get; set; }
    public bool IsSitting => LastPoseChangeTick < 0;
    public bool Dashing { get; private set; }

    public Camel()
    {
        Type = EntityType.Camel;
        HorseMask = HorseMask.Tamed;
    }

    protected override bool UsesAi => true;
    protected override int MaximumPassengers => 2;
    protected override VectorD PassengerOffset(int index)
    {
        var yaw = Yaw.Degrees * MathF.PI / 180;
        var offset = index == 0 ? 0.5f : -0.7f;
        return new VectorD(-MathF.Sin(yaw) * offset, Dimension.Height - (IsSitting ? 1.43f : 0), MathF.Cos(yaw) * offset);
    }
    protected virtual float DashSpeed => 22.2222f;
    protected override string? SoundName => "camel";
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.Cactus };
    protected override int GetExperienceReward() => IsBaby ? 0 : Random.Next(1, 4);

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new PanicGoal(this, 1.2f));
        actions.AddGoal(2, new BreedGoal(this));
        actions.AddGoal(3, new TemptGoal(this, CanEat, 1.25f));
        actions.AddGoal(4, new FollowParentGoal(this));
        actions.AddGoal(5, new CamelSitGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 1));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
    }

    internal void SetSitting(bool sitting)
    {
        if (IsSitting == sitting || sitting && (HasRider || IsBaby || InWater || InLava))
            return;
        var time = Math.Max(1, Level.LevelData.Time);
        LastPoseChangeTick = sitting ? -time : time;
        Pose = sitting ? Pose.Sitting : Pose.Standing;
        if (sitting)
            (Navigator as Navigator)?.Stop();
        SynchronizeMetadata();
    }

    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Health <= 0 || player.Level != Level || player.GameMode == GameMode.Spectator ||
            !IsInRange(player, 4) || !CanSee(player))
            return;
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (CanEat(item))
        {
            if (Health >= GetAttributeValue("minecraft:generic.max_health") && !IsBaby && (Age != 0 || LoveTicks > 0))
                return;
            Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 2);
            if (IsBaby)
                Age = Math.Min(0, Age + 200);
            else if (Age == 0 && LoveTicks == 0)
            {
                LoveTicks = 600;
                SendEntityEvent(18);
            }
            await ConsumeInteractionItemAsync(player, hand);
            SynchronizeMetadata();
            return;
        }
        if (IsBaby)
            return;
        if (!HasSaddle && item is { Count: > 0, Type: Material.Saddle })
        {
            HasSaddle = true;
            await ConsumeInteractionItemAsync(player, hand);
            return;
        }
        if (player.Sneaking)
        {
            if (item == null || item.IsAir)
                SetSitting(!IsSitting);
            return;
        }
        if (player is Player rider && Mount(rider))
        {
            GoalController?.Pause();
            SetSitting(false);
        }
    }

    internal bool RequestDash(IPlayer player, int charge)
    {
        if (!ReferenceEquals(Rider, player) || !HasSaddle || IsSitting || DashCooldown > 0 ||
            !MovementFlags.HasFlag(MovementFlags.OnGround) || InWater || InLava || MobBitMask.HasFlag(MobBitmask.NoAi))
            return false;
        return Interlocked.CompareExchange(ref pendingDashCharge, Math.Clamp(charge, 0, 100), -1) == -1;
    }

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (DashCooldown > 0 && --DashCooldown == 0)
            PlayMobSound("dash_ready");
        if (Dashing && ++dashTicks >= 5 && (MovementFlags.HasFlag(MovementFlags.OnGround) || InWater || InLava || dashTicks >= 12))
        {
            Dashing = false;
            SynchronizeMetadata();
        }
    }

    protected override ValueTask OnHurtAsync(IEntity source)
    {
        SetSitting(false);
        return default;
    }

    protected override void TickRidden()
    {
        // Camel controls replace the horse's taming and vertical jump behavior.
        foreach (var passenger in Passengers.ToArray())
            if (passenger.Level != Level || !passenger.Alive || passenger.Sneaking || !Alive) Dismount(passenger);
        var charge = Interlocked.Exchange(ref pendingDashCharge, -1);
        if (Rider == null)
        {
            if (GoalController is GoalSelector { IsPaused: true } goals)
                goals.Resume();
            if (IsSitting)
            {
                MoveControl.Stop();
                Motion = new VectorD(0, Motion.Y, 0);
            }
            return;
        }
        if (!HasSaddle || MobBitMask.HasFlag(MobBitmask.NoAi))
            return;
        SetSitting(false);
        Yaw = Rider.Yaw;
        Pitch = Rider.Pitch.Degrees * 0.5f;
        var input = Rider.Input;
        var forward = (input.HasFlag(PlayerInput.Forward) ? 1f : 0) - (input.HasFlag(PlayerInput.Backward) ? 1f : 0);
        var sideways = (input.HasFlag(PlayerInput.Left) ? 0.5f : 0) - (input.HasFlag(PlayerInput.Right) ? 0.5f : 0);
        MoveControl.Ride(MovementSpeed + (input.HasFlag(PlayerInput.Sprint) ? 0.1f : 0),
            forward < 0 ? forward * 0.25f : forward, sideways);
        if (charge < 0 || DashCooldown > 0 || !MovementFlags.HasFlag(MovementFlags.OnGround) || InWater || InLava)
            return;
        var strength = charge >= 90 ? 1 : 0.4f + 0.4f * charge / 90;
        var yaw = Yaw.Degrees * MathF.PI / 180;
        Motion += new VectorD(-MathF.Sin(yaw) * DashSpeed * MovementSpeed * strength,
            1.4285f * JumpPower * strength, MathF.Cos(yaw) * DashSpeed * MovementSpeed * strength);
        DashCooldown = DashCooldownTicks;
        dashTicks = 0;
        Dashing = true;
        PlayMobSound("dash");
        SynchronizeMetadata();
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean);
        writer.WriteBoolean(Dashing);
        writer.WriteEntityMetadataType(19, EntityMetadataType.VarLong);
        writer.WriteVarLong(LastPoseChangeTick);
    }
}

internal sealed class CamelSitGoal(Camel camel) : Goal
{
    private long endTick;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look | GoalFlags.Jump;
    public override bool CanUse() => !camel.HasRider && !camel.IsBaby && !camel.InWater && !camel.InLava &&
        camel.MovementFlags.HasFlag(MovementFlags.OnGround) && camel.AiTick - camel.LastHurtTick > 100 &&
        (camel.IsSitting || camel.Random.Next(500) == 0);
    public override bool CanContinue() => camel.IsSitting && !camel.HasRider && !camel.InWater && !camel.InLava && camel.AiTick < endTick;
    public override void Start()
    {
        endTick = camel.AiTick + camel.Random.Next(200, 601);
        camel.SetSitting(true);
    }
    public override void Stop() => camel.SetSitting(false);
}
