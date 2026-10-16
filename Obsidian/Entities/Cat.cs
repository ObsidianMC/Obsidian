using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:cat")]
public sealed partial class Cat : Animal
{
    internal static readonly string[] VariantNames = ["all_black", "black", "british_shorthair", "calico", "jellie", "persian",
        "ragdoll", "red", "siamese", "tabby", "white"];
    internal static readonly string[] DyeNames = ["white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"];
    private int variant;
    private byte collarColor = 14;
    public Cat() => Type = EntityType.Cat;
    public Guid Owner { get; set; }
    public bool Tamed => Owner != Guid.Empty;
    public bool OrderedToSit { get; set; }
    internal bool IsSitting { get; set; }
    internal bool IsLying { get; private set; }
    internal bool IsRelaxing { get; private set; }
    internal void SetResting(bool lying, bool relaxing)
    {
        if (IsLying == lying && IsRelaxing == relaxing) return;
        IsLying = lying;
        IsRelaxing = relaxing;
        SynchronizeMetadata();
    }
    public int Variant
    {
        get => variant;
        set => variant = (uint)value < VariantNames.Length ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    public byte CollarColor
    {
        get => collarColor;
        set => collarColor = value < DyeNames.Length ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    protected override bool UsesAi => true;
    protected override string? SoundName => "cat";
    protected override bool CanEat(ItemStack? item) =>
        item is { Count: > 0, Holder.Id: ItemIds.Cod or ItemIds.Salmon };
    protected override int GetExperienceReward() => IsBaby ? 0 : Random.Next(1, 4);
    protected override void FinalizeSpawn() => Variant = Random.Next(VariantNames.Length);

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new CatSitGoal(this));
        actions.AddGoal(2, new PanicGoal(this, 1.33f));
        actions.AddGoal(3, new CatRelaxOnOwnerGoal(this));
        actions.AddGoal(3, new TemptGoal(this, CanEat, 0.6f));
        actions.AddGoal(4, new BreedGoal(this));
        actions.AddGoal(5, new AvoidEntityGoal(this, entity => !Tamed && entity is IPlayer player &&
            player.GameMode is not GameMode.Creative and not GameMode.Spectator &&
            !CanEat(player.GetHeldItem()) && !CanEat(player.GetOffHandItem()), 16, 1.33f));
        actions.AddGoal(6, new CatFollowOwnerGoal(this));
        actions.AddGoal(7, new FollowParentGoal(this));
        actions.AddGoal(8, new RandomStrollGoal(this, 0.8f));
        actions.AddGoal(9, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(10, new RandomLookAroundGoal(this));
    }

    internal override async ValueTask<bool> FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Health <= 0 || player.Level != Level || player.GameMode == GameMode.Spectator ||
            !IsInRange(player, 3) || !CanSee(player))
            return false;
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (CanEat(item))
        {
            if (!Tamed)
            {
                await ConsumeInteractionItemAsync(player, hand);
                if (Random.Next(3) == 0)
                {
                    Owner = player.Uuid;
                    PersistenceRequired = true;
                    OrderedToSit = true;
                }
                SendEntityEvent(Tamed ? (byte)7 : (byte)6);
                SynchronizeMetadata();
                return true;
            }
            if (Health < GetAttributeValue("minecraft:generic.max_health"))
            {
                Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 2);
                await ConsumeInteractionItemAsync(player, hand);
                SynchronizeMetadata();
                return true;
            }
            if (IsBaby || Age == 0 && LoveTicks == 0)
            {
                OrderedToSit = false;
                return await base.FeedAsync(player, hand);
            }
        }
        if (!Tamed || Owner != player.Uuid)
            return false;
        if (item is { Count: > 0 })
        {
            var color = Array.FindIndex(DyeNames, name => item.Holder.UnlocalizedName == $"minecraft:{name}_dye");
            if (color >= 0)
            {
                // Vanilla's cat takes the interaction anyway (it sits down or stands up).
                if (color == CollarColor)
                    return true;
                CollarColor = (byte)color;
                await ConsumeInteractionItemAsync(player, hand);
                SynchronizeMetadata();
                return true;
            }
        }
        OrderedToSit = !OrderedToSit;
        if (OrderedToSit)
            (Navigator as Navigator)?.Stop();
        SynchronizeMetadata();
        return true;
    }

    protected override ValueTask OnHurtAsync(IEntity source)
    {
        OrderedToSit = false;
        return default;
    }

    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Cat)base.CreateOffspring(mate);
        child.Owner = Owner;
        child.PersistenceRequired = Tamed;
        child.Variant = Random.Next(2) == 0 ? Variant : ((Cat)mate).Variant;
        child.CollarColor = CollarColor;
        child.SynchronizeMetadata();
        return child;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        writer.WriteByte((byte)((Tamed ? 4 : 0) | (IsSitting ? 1 : 0)));
        writer.WriteEntityMetadataType(18, EntityMetadataType.OptionalLivingEntityReference);
        writer.WriteOptional(Tamed ? (Guid?)Owner : null);
        writer.WriteEntityMetadataType(19, EntityMetadataType.CatVariant);
        writer.WriteVarInt(Variant);
        writer.WriteEntityMetadataType(20, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsLying);
        writer.WriteEntityMetadataType(21, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsRelaxing);
        writer.WriteEntityMetadataType(22, EntityMetadataType.VarInt);
        writer.WriteVarInt(CollarColor);
    }
}

internal sealed class CatSitGoal(Cat cat) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Jump;
    public override bool CanUse() => cat.Tamed && cat.OrderedToSit && !cat.InWater &&
        cat.MovementFlags.HasFlag(MovementFlags.OnGround);
    public override void Start()
    {
        var navigation = (Navigator)cat.Navigator!;
        navigation.Stop();
        navigation.IsPaused = true;
        cat.IsSitting = true;
        cat.SynchronizeMetadata();
    }
    public override void Stop()
    {
        var navigation = (Navigator)cat.Navigator!;
        navigation.Stop();
        navigation.IsPaused = false;
        cat.IsSitting = false;
        cat.SynchronizeMetadata();
    }
}

internal sealed class CatFollowOwnerGoal(Cat cat) : NavigationGoal(cat, 1)
{
    private IPlayer? owner;
    private long nextPathTick;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool CanUse()
    {
        if (!cat.Tamed || cat.OrderedToSit)
            return false;
        owner = cat.Level.Players.Values.FirstOrDefault(player => player.Uuid == cat.Owner && player.Health > 0 &&
            player.GameMode != GameMode.Spectator);
        return owner != null && (owner.Position - cat.Position).MagnitudeSquared() >= 100;
    }
    public override bool CanContinue() => owner != null && owner.Level == cat.Level && owner.Health > 0 &&
        owner.GameMode != GameMode.Spectator && owner.Uuid == cat.Owner && cat.Tamed && !cat.OrderedToSit &&
        (owner.Position - cat.Position).MagnitudeSquared() > 25 && Navigation.IsNavigating;
    public override void Start()
    {
        nextPathTick = cat.AiTick;
    }
    public override ValueTask TickAsync()
    {
        var teleport = (owner!.Position - cat.Position).MagnitudeSquared() >= 144;
        if (!teleport) cat.LookControl.LookAt(owner);
        if (cat.AiTick >= nextPathTick)
        {
            nextPathTick = cat.AiTick + 10;
            if (teleport) TryTeleport();
            else MoveTo(owner);
        }
        return default;
    }
    private void TryTeleport()
    {
        if (cat.Level is not AbstractLevel level) return;
        var origin = (Vector)owner!.Position.Floor();
        var evaluator = new WalkNodeEvaluator(cat);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var x = cat.Random.Next(-3, 4);
            var z = cat.Random.Next(-3, 4);
            if (Math.Abs(x) < 2 && Math.Abs(z) < 2) continue;
            var point = origin + new Vector(x, cat.Random.Next(-1, 2), z);
            var floor = cat.Terrain.GetBlock(point - Vector.Up);
            var destination = new VectorD(point.X + 0.5, point.Y, point.Z + 0.5);
            var bounds = cat.Dimension.CreateBBFromPosition(destination);
            if (floor == null || TagsRegistry.Block.Leaves.Entries.Contains(floor.RegistryId) ||
                evaluator.FindGround(point.X, point.Z, point.Y) is not { } ground || ground.Y != point.Y ||
                evaluator.GetCost(destination) != 0 || !cat.Terrain.IsFree(bounds) || !cat.Terrain.IsFreeOfFluid(bounds) ||
                !level.TryMoveEntity(cat, cat.Position, destination)) continue;
            cat.CompleteTeleport(destination);
            Navigation.Stop();
            return;
        }
    }
    public override void Stop()
    {
        base.Stop();
        owner = null;
    }
}

internal sealed class CatRelaxOnOwnerGoal(Cat cat) : NavigationGoal(cat, 1.1f)
{
    private IPlayer? owner;
    private Vector goal;
    private int onBedTicks;
    public override GoalFlags Flags => GoalFlags.None;
    public override bool CanUse()
    {
        if (!cat.Tamed || cat.OrderedToSit) return false;
        owner = cat.Level.Players.Values.FirstOrDefault(player => player.Uuid == cat.Owner);
        if (owner == null || !owner.Sleeping || (owner.Position - cat.Position).MagnitudeSquared() > 100) return false;
        var position = (Vector)owner.Position.Floor();
        var bed = cat.Terrain.GetBlock(position);
        if (bed == null || !TagsRegistry.Block.Beds.Entries.Contains(bed.RegistryId)) return false;
        goal = position + (bed.GetProperty("facing") switch
        {
            "north" => new Vector(0, 0, 1),
            "south" => new Vector(0, 0, -1),
            "west" => new Vector(1, 0, 0),
            "east" => new Vector(-1, 0, 0),
            _ => Vector.Zero
        });
        return !SpaceIsOccupied();
    }
    private bool SpaceIsOccupied() => cat.GetEntitiesNear(16).OfType<Cat>().Any(other => other != cat &&
        (other.IsLying || other.IsRelaxing) && other.BoundingBox.Max.X > goal.X - 2 && other.BoundingBox.Min.X < goal.X + 3 &&
        other.BoundingBox.Max.Y > goal.Y - 2 && other.BoundingBox.Min.Y < goal.Y + 3 &&
        other.BoundingBox.Max.Z > goal.Z - 2 && other.BoundingBox.Min.Z < goal.Z + 3);
    public override bool CanContinue() => cat.Tamed && !cat.OrderedToSit && owner is { Sleeping: true } &&
        owner.Level == cat.Level && owner.Uuid == cat.Owner && !SpaceIsOccupied();
    public override void Start()
    {
        cat.IsSitting = false;
        cat.SynchronizeMetadata();
        MoveTo(new VectorD(goal.X, goal.Y, goal.Z));
    }
    public override ValueTask TickAsync()
    {
        cat.IsSitting = false;
        MoveTo(new VectorD(goal.X, goal.Y, goal.Z));
        if ((owner!.Position - cat.Position).MagnitudeSquared() < 2.5)
        {
            if (++onBedTicks > 8) cat.SetResting(true, false);
            else
            {
                cat.LookControl.LookAt(owner);
                cat.SetResting(cat.IsLying, true);
            }
        }
        else cat.SetResting(false, cat.IsRelaxing);
        return default;
    }
    public override void Stop()
    {
        cat.SetResting(false, false);
        onBedTicks = 0;
        owner = null;
        base.Stop();
    }
}
