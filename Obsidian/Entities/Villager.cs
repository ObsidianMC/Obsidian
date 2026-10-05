using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:villager")]
public sealed partial class Villager : AgeableMob
{
    public Villager()
    {
        Type = EntityType.Villager;
        PersistenceRequired = true;
    }

    protected override bool UsesAi => true;
    protected override string? SoundName => "villager";

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        ((Navigator)Navigator!).CanOpenDoors = true;
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new PanicGoal(this, 1.2f));
        actions.AddGoal(2, new VillagerAvoidThreatGoal(this));
        actions.AddGoal(3, new VillagerShelterGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 0.6f));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
    }

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi) || AiTick % 10 != 0 ||
            !MovementFlags.HasFlag(MovementFlags.HorizontalCollision) || Navigator is not Navigator { IsNavigating: true })
            return;
        var origin = (Vector)Position.Floor();
        for (var x = origin.X - 1; x <= origin.X + 1; x++)
        for (var z = origin.Z - 1; z <= origin.Z + 1; z++)
        {
            var point = new Vector(x, origin.Y, z);
            var block = Terrain.GetBlock(point);
            if (block == null || !TagsRegistry.Block.WoodenDoors.Entries.Contains(block.RegistryId) || block.GetProperty("open") != "false")
                continue;
            if (block.GetProperty("half") == "upper")
            {
                point -= new Vector(0, 1, 0);
                block = Terrain.GetBlock(point);
            }
            var upper = Terrain.GetBlock(point + new Vector(0, 1, 0));
            if (block == null || upper == null || upper.RegistryId != block.RegistryId)
                continue;
            await Level.SetBlockAsync(point, block.WithProperty("open", true), true);
            await Level.SetBlockAsync(point + new Vector(0, 1, 0), upper.WithProperty("open", true), true);
        }
    }
}

internal sealed class VillagerAvoidThreatGoal(Villager villager) : NavigationGoal(villager, 1.2f)
{
    private IEntity? threat;
    private VectorD destination;
    public override bool CanUse()
    {
        if (villager.Random.Next(5) != 0)
            return false;
        threat = villager.GetEntitiesNear(12)
            .Where(entity => entity.Type is EntityType.Zombie or EntityType.Husk or EntityType.Drowned or
                EntityType.ZombieVillager or EntityType.Pillager or EntityType.Vindicator or EntityType.Evoker or EntityType.Ravager)
            .Where(entity => villager.IsValidTarget(entity) && villager.CanSee(entity))
            .MinBy(entity => (entity.Position - villager.Position).MagnitudeSquared());
        if (threat == null || RandomPosition.Find(villager, 16, threat.Position) is not VectorD point ||
            (point - threat.Position).MagnitudeSquared() <= (villager.Position - threat.Position).MagnitudeSquared())
            return false;
        destination = point;
        return true;
    }
    public override void Start() => MoveTo(destination);
    public override bool CanContinue() => threat != null && villager.IsValidTarget(threat) &&
        villager.IsInRange(threat, 16) && Navigation.IsNavigating;
    public override void Stop()
    {
        base.Stop();
        threat = null;
    }
}

internal sealed class VillagerShelterGoal(Villager villager) : NavigationGoal(villager, 0.8f)
{
    private VectorD destination;
    public override bool CanUse()
    {
        if (villager.Level.DimensionName != "minecraft:overworld" ||
            !villager.Level.LevelData.Raining && villager.Level.DayTime is >= 0 and < 12000 ||
            villager.Terrain.GetSkyLight((Vector)villager.Position.Floor()) < 15 || villager.Random.Next(20) != 0)
            return false;
        var evaluator = new WalkNodeEvaluator(villager);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var x = (int)Math.Floor(villager.Position.X) + villager.Random.Next(-16, 17);
            var z = (int)Math.Floor(villager.Position.Z) + villager.Random.Next(-16, 17);
            if (evaluator.FindGround(x, z, villager.Position.Y + villager.Random.Next(-3, 4)) is VectorD point &&
                villager.Terrain.GetSkyLight((Vector)point.Floor()) < 15)
            {
                destination = point;
                return true;
            }
        }
        return false;
    }
    public override void Start() => MoveTo(destination);
    public override bool CanContinue() => Navigation.IsNavigating;
}
