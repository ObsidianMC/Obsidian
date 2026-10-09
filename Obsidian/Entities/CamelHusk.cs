using Obsidian.API.Inventory;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:camel_husk")]
public sealed partial class CamelHusk : Camel
{
    public CamelHusk() => Type = EntityType.CamelHusk;
    protected override string? SoundName => "camel_husk";
    protected override bool CanDespawn => true;
    protected override float DashSpeed => 4;
    internal override bool CanBreed => false;
    internal override bool CanMateWith(Animal mate) => false;
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0 } && TagsRegistry.Item.CamelHuskFood.Entries.Contains(item.Holder.Id);
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new PanicGoal(this, 1.2f));
        actions.AddGoal(3, new TemptGoal(this, CanEat, 1.25f));
        actions.AddGoal(5, new CamelSitGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 1));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 6));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
    }
    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Health <= 0 || player.Level != Level || player.GameMode == GameMode.Spectator ||
            !IsInRange(player, 4) || !CanSee(player)) return;
        SetSitting(false);
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (CanEat(item))
        {
            if (Health >= GetAttributeValue("minecraft:generic.max_health")) return;
            Health = Math.Min(GetAttributeValue("minecraft:generic.max_health"), Health + 2);
            await ConsumeInteractionItemAsync(player, hand);
            SynchronizeMetadata();
            return;
        }
        await base.FeedAsync(player, hand);
    }
    protected override async ValueTask OnDeathAsync(IEntity source)
    {
        await base.OnDeathAsync(source);
        DropItem(Material.RottenFlesh, Random.Next(3));
    }
}
