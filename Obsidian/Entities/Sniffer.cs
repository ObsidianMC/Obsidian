using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:sniffer")]
public sealed partial class Sniffer : FarmAnimal
{
    private readonly Queue<Vector> explored = new();
    private long nextDig;
    internal int State { get; private set; }
    internal int DropSeedAt { get; private set; }
    public Sniffer() => Type = EntityType.Sniffer;
    protected override string? SoundName => "sniffer";
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (idleStateTicks > 0)
        {
            if (--idleStateTicks == 0 || !CanDig) { idleStateTicks = 0; ChangeState(0); }
            return;
        }
        if (State != 0 || IsBaby || !CanDig) return;
        if (Random.Next(1000) == 0) { ChangeState(2); idleStateTicks = Random.Next(40, 81); PlayMobSound("scenting"); }
    }
    private int idleStateTicks;
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.TorchflowerSeeds };
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(5, new SnifferDigGoal(this));
    }
    protected override IEntity CreateOffspring(Animal mate)
    {
        var egg = new ItemEntity { Level = Level, EntityId = Server.GetNextEntityId(), Position = Position,
            Item = ItemsRegistry.GetSingleItem(Material.SnifferEgg), CanPickup = false };
        Level.SpawnEntity(egg);
        return egg;
    }
    internal bool CanDig => !IsBaby && LoveTicks == 0 && AiTick >= nextDig && AiTick - LastHurtTick > 100 && !InWater && !InLava;
    internal bool IsDiggable(Vector point) => Terrain.GetBlock(point - new Vector(0, 1, 0)) is { } floor &&
        TagsRegistry.Block.SnifferDiggableBlock.Entries.Contains(floor.RegistryId) && !explored.Contains(point);
    internal void ChangeState(int state, int dropAt = 0)
    {
        State = state;
        DropSeedAt = dropAt;
        SynchronizeMetadata();
        if (state == 5) PlayMobSound("digging");
    }
    internal void FinishDig(Vector point)
    {
        explored.Enqueue(point);
        while (explored.Count > 20) explored.Dequeue();
        nextDig = AiTick + 9600;
    }
    internal void DropSeed()
    {
        var table = LootTables.All["minecraft:gameplay/sniffer_digging"];
        foreach (var item in table.GetRandomItems(new LootContext { Random = table.CreateRandom(Random.NextInt64()), Origin = Position, ThisEntity = this })) DropItem(item);
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.SnifferState); writer.WriteVarInt(State);
        writer.WriteEntityMetadataType(18, EntityMetadataType.VarInt); writer.WriteVarInt(DropSeedAt);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteLong("ObsidianDigCooldown", Math.Max(0, nextDig - AiTick));
        writer.WriteArray("ObsidianExplored", explored.SelectMany(point => new[] { point.X, point.Y, point.Z }).ToArray());
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        nextDig = AiTick + Math.Clamp(tag.TryGetTagValue<long>("ObsidianDigCooldown", out var cooldown) ? cooldown : 0, 0, 9600);
        if (tag.TryGetTag<NbtArray<int>>("ObsidianExplored", out var saved))
        {
            var points = saved.GetArray();
            for (var i = 0; i + 2 < points.Length && explored.Count < 20; i += 3) explored.Enqueue(new Vector(points[i], points[i + 1], points[i + 2]));
        }
    }
}
internal sealed class SnifferDigGoal(Sniffer sniffer) : NavigationGoal(sniffer, 1)
{
    private Vector point;
    private int ticks;
    private bool dropped;
    public override bool RequiresUpdateEveryTick => true;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool CanUse()
    {
        if (!sniffer.CanDig || sniffer.State != 0 || sniffer.Random.Next(100) != 0) return false;
        var origin = (Vector)sniffer.Position.Floor();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            point = origin + new Vector(sniffer.Random.Next(-8, 9), 0, sniffer.Random.Next(-8, 9));
            if (sniffer.IsDiggable(point) && sniffer.Terrain.IsFree(sniffer.Dimension.CreateBBFromPosition(new VectorD(point.X + 0.5, point.Y, point.Z + 0.5)))) return true;
        }
        return false;
    }
    public override void Start() { ticks = 400; dropped = false; sniffer.ChangeState(4); MoveTo(new VectorD(point.X + 0.5, point.Y, point.Z + 0.5)); }
    public override bool CanContinue() => ticks > 0 && !sniffer.IsBaby && sniffer.LoveTicks == 0 && sniffer.AiTick - sniffer.LastHurtTick > 100 && !sniffer.InWater && !sniffer.InLava;
    public override ValueTask TickAsync()
    {
        ticks--;
        if (sniffer.State == 4 && (sniffer.Position - new VectorD(point.X + 0.5, point.Y, point.Z + 0.5)).MagnitudeSquared() < 2 && sniffer.IsDiggable(point))
        { Navigation.Stop(); sniffer.MoveControl.Stop(); sniffer.ChangeState(5, (int)sniffer.AiTick + 100); ticks = 120; }
        else if (sniffer.State == 5)
        {
            if (!dropped && ticks <= 20) { dropped = true; sniffer.DropSeed(); sniffer.FinishDig(point); }
            if (ticks == 0) { sniffer.ChangeState(6); ticks = 40; }
        }
        return default;
    }
    public override void Stop() { base.Stop(); sniffer.ChangeState(0); }
}
