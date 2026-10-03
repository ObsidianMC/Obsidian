using Obsidian.API.Inventory;
using Obsidian.Entities.AI;

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
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.Cod or Material.Salmon };
    protected override int GetExperienceReward() => IsBaby ? 0 : Random.Next(1, 4);
    protected override void FinalizeSpawn() => Variant = Random.Next(VariantNames.Length);

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new CatSitGoal(this));
        actions.AddGoal(2, new PanicGoal(this, 1.33f));
        actions.AddGoal(3, new TemptGoal(this, CanEat, 0.6f));
        actions.AddGoal(4, new BreedGoal(this));
        actions.AddGoal(5, new AvoidEntityGoal(this, entity => !Tamed && entity is IPlayer player &&
            player.Gamemode is not Gamemode.Creative and not Gamemode.Spectator &&
            !CanEat(player.GetHeldItem()) && !CanEat(player.GetOffHandItem()), 16, 1.33f));
        actions.AddGoal(6, new CatFollowOwnerGoal(this));
        actions.AddGoal(7, new FollowParentGoal(this));
        actions.AddGoal(8, new RandomStrollGoal(this, 0.8f));
        actions.AddGoal(9, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(10, new RandomLookAroundGoal(this));
    }

    internal override async ValueTask FeedAsync(IPlayer player, Hand hand)
    {
        if (!Alive || IsRemoved || player.Health <= 0 || player.Level != Level || player.Gamemode == Gamemode.Spectator ||
            !IsInRange(player, 3) || !CanSee(player))
            return;
        var item = hand == Hand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
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
                return;
            }
            if (Health < GetAttributeValue("minecraft:generic.max_health"))
            {
                Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 2);
                await ConsumeInteractionItemAsync(player, hand);
                SynchronizeMetadata();
                return;
            }
            if (IsBaby || Age == 0 && LoveTicks == 0)
            {
                OrderedToSit = false;
                await base.FeedAsync(player, hand);
                return;
            }
        }
        if (!Tamed || Owner != player.Uuid)
            return;
        if (item is { Count: > 0 })
        {
            var color = Array.FindIndex(DyeNames, name => item.Holder.UnlocalizedName == $"minecraft:{name}_dye");
            if (color >= 0)
            {
                if (color == CollarColor)
                    return;
                CollarColor = (byte)color;
                await ConsumeInteractionItemAsync(player, hand);
                SynchronizeMetadata();
                return;
            }
        }
        OrderedToSit = !OrderedToSit;
        if (OrderedToSit)
            (Navigator as Navigator)?.Stop();
        SynchronizeMetadata();
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
        writer.WriteBoolean(false);
        writer.WriteEntityMetadataType(21, EntityMetadataType.Boolean);
        writer.WriteBoolean(false);
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
            player.Gamemode != Gamemode.Spectator);
        return owner != null && (owner.Position - cat.Position).MagnitudeSquared() > 100;
    }
    public override bool CanContinue() => owner != null && owner.Level == cat.Level && owner.Health > 0 &&
        owner.Gamemode != Gamemode.Spectator && owner.Uuid == cat.Owner && cat.Tamed && !cat.OrderedToSit &&
        (owner.Position - cat.Position).MagnitudeSquared() > 4 && Navigation.IsNavigating;
    public override void Start()
    {
        nextPathTick = cat.AiTick + 10;
        MoveTo(owner!);
    }
    public override ValueTask TickAsync()
    {
        cat.LookControl.LookAt(owner!);
        if (cat.AiTick >= nextPathTick)
        {
            nextPathTick = cat.AiTick + 10;
            MoveTo(owner!);
        }
        return default;
    }
    public override void Stop()
    {
        base.Stop();
        owner = null;
    }
}
