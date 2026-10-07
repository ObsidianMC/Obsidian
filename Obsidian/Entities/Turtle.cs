using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:turtle")]
public sealed partial class Turtle : FarmAnimal
{
    internal Vector Home { get; private set; }
    internal bool HasEgg { get; private set; }
    internal bool Laying { get; set; }
    public Turtle() => Type = EntityType.Turtle;
    internal void SetHome(Vector point) => Home = point;
    internal override bool SwimmingNavigation => true;
    protected override bool UsesFloatGoal => false;
    protected override string? SoundName => "turtle";
    protected override float DimensionScale => IsBaby ? 0.3f : 1;
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.Seagrass };
    internal override bool CanBreed => !HasEgg && base.CanBreed;
    protected override VectorD Travel() => VolumeMovement.Travel(this, InWater);
    protected override ValueTask TickAirSupplyAsync() { Air = 300; return default; }
    protected override void FinalizeSpawn() => Home = (Vector)Position.Floor();
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(3, new TurtleLayEggGoal(this));
        actions.AddGoal(5, new SeekWaterGoal(this));
    }
    protected override IEntity CreateOffspring(Animal mate) { HasEgg = true; SynchronizeMetadata(); return this; }
    protected override async ValueTask TickMobAsync()
    {
        var baby = IsBaby; await base.TickMobAsync(); if (baby && !IsBaby) DropItem(Material.TurtleScute);
    }
    internal async ValueTask LayEggAsync(Vector point)
    {
        if (Terrain.GetBlock(point)?.IsAir != true || Terrain.GetBlock(point - new Vector(0, 1, 0))?.Material is not (Material.Sand or Material.RedSand)) return;
        await Level.SetBlockAsync(point, BlocksRegistry.Get(Material.TurtleEgg).WithProperty("eggs", Random.Next(1, 5)), true);
        HasEgg = false; Laying = false; LoveTicks = 0; SynchronizeMetadata();
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    { if (!IsBaby) DropItem(Material.Seagrass, Random.Next(3)); return default; }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean); writer.WriteBoolean(HasEgg);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean); writer.WriteBoolean(Laying);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    { writer.WriteBool("has_egg", HasEgg); writer.WriteArray("home_pos", new[] { Home.X, Home.Y, Home.Z }); }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        HasEgg = tag.TryGetBool("has_egg", out var egg) && egg;
        Home = (Vector)Position.Floor();
        if (tag.TryGetTag<NbtArray<int>>("home_pos", out var home) && home.Count == 3)
        { var coordinates = home.GetArray(); Home = new Vector(coordinates[0], coordinates[1], coordinates[2]); }
    }
}
internal sealed class TurtleLayEggGoal(Turtle turtle) : NavigationGoal(turtle, 1)
{
    private Vector point;
    private int ticks;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse()
    {
        if (!turtle.HasEgg) return false;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            point = turtle.Home + new Vector(turtle.Random.Next(-8, 9), turtle.Random.Next(-2, 3), turtle.Random.Next(-8, 9));
            if (turtle.Terrain.GetBlock(point)?.IsAir == true && turtle.Terrain.GetBlock(point - new Vector(0, 1, 0))?.Material is Material.Sand or Material.RedSand) return true;
        }
        return false;
    }
    public override void Start() { ticks = 0; MoveTo(new VectorD(point.X + 0.5, point.Y, point.Z + 0.5)); }
    public override bool CanContinue() => turtle.HasEgg && turtle.AiTick - turtle.LastHurtTick > 100 && ticks < 1000;
    public override async ValueTask TickAsync()
    {
        ticks++;
        var target = new VectorD(point.X + 0.5, point.Y, point.Z + 0.5);
        if ((target - turtle.Position).MagnitudeSquared() > 2) { MoveTo(target); return; }
        if (turtle.InWater || turtle.Terrain.GetBlock(point)?.IsAir != true) return;
        Navigation.Stop(); turtle.MoveControl.Stop();
        if (!turtle.Laying) { turtle.Laying = true; ticks = 0; turtle.SynchronizeMetadata(); }
        if (ticks >= 200) await turtle.LayEggAsync(point);
    }
    public override void Stop() { base.Stop(); turtle.Laying = false; turtle.SynchronizeMetadata(); }
}
