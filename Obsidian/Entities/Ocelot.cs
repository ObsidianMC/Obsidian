using Obsidian.API.Inventory;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:ocelot")]
public sealed partial class Ocelot : Animal
{
    public Ocelot() => Type = EntityType.Ocelot;
    public bool Trusting { get; set; }
    protected override bool UsesAi => true;
    protected override string? SoundName => "ocelot";
    protected internal override float AttackDamage => 3;
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.Cod or Material.Salmon };
    protected override int GetExperienceReward() => IsBaby ? 0 : Random.Next(1, 4);

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new PanicGoal(this, 1.33f));
        actions.AddGoal(2, new TemptGoal(this, CanEat, 0.6f));
        actions.AddGoal(3, new BreedGoal(this));
        actions.AddGoal(4, new AvoidEntityGoal(this, entity => !Trusting && entity is IPlayer player &&
            player.Gamemode is not Gamemode.Creative and not Gamemode.Spectator &&
            !CanEat(player.GetHeldItem()) && !CanEat(player.GetOffHandItem()), 16, 1.33f));
        actions.AddGoal(5, new MeleeAttackGoal(this, 1.2f));
        actions.AddGoal(6, new FollowParentGoal(this));
        actions.AddGoal(7, new RandomStrollGoal(this, 0.8f));
        actions.AddGoal(8, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(9, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, entity => entity.Type == EntityType.Chicken));
    }

    internal override async ValueTask FeedAsync(IPlayer player, Hand hand)
    {
        if (!Alive || IsRemoved || player.Health <= 0 || player.Level != Level || player.Gamemode == Gamemode.Spectator ||
            !IsInRange(player, 3) || !CanSee(player))
            return;
        var item = hand == Hand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (!Trusting && CanEat(item))
        {
            await ConsumeInteractionItemAsync(player, hand);
            Trusting = Random.Next(3) == 0;
            if (Trusting)
                PersistenceRequired = true;
            SendEntityEvent(Trusting ? (byte)41 : (byte)40);
            SynchronizeMetadata();
            return;
        }
        await base.FeedAsync(player, hand);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean);
        writer.WriteBoolean(Trusting);
    }
}
