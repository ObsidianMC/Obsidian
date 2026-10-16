using Obsidian.API.Inventory;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:mule")]
public sealed partial class Mule : ChestedHorse
{
    public Mule() => Type = EntityType.Mule;
    protected override bool UsesAi => true;
    protected override string? SoundName => "mule";
    protected override bool CanEat(ItemStack? item) => item is
    {
        Count: > 0,
        Holder.Id: ItemIds.Wheat
            or ItemIds.Sugar
            or ItemIds.Apple
            or ItemIds.HayBlock
            or ItemIds.GoldenCarrot
            or ItemIds.GoldenApple
    };
    internal override bool CanBreed => false;
    protected override bool IsBreedingFood(Item food) => false;
    protected override void RegisterGoals(AI.GoalSelector actions, AI.GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(3, new AI.FollowParentGoal(this));
        actions.AddGoal(5, new HorseGrazeGoal(this));
    }

    protected override void FinalizeSpawn()
    {
        var health = 15 + Random.Next(8) + Random.Next(9);
        TryUpdateAttribute("minecraft:generic.max_health", health);
        TryUpdateAttribute("minecraft:generic.movement_speed", 0.175f);
        TryUpdateAttribute("minecraft:horse.jump_strength", 0.5f);
        Health = health;
    }
}
