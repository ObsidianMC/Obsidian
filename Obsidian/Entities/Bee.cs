using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:bee")]
public sealed partial class Bee : FarmAnimal
{
    private int pollinationTicks;
    private int stingTicks;
    private int searchCooldown;
    public Bee() => Type = EntityType.Bee;
    public bool HasNectar { get; internal set; }
    public bool HasStung { get; internal set; }
    public int AngerTicks { get; internal set; }
    public Vector? HivePosition { get; internal set; }
    public Vector? FlowerPosition { get; internal set; }
    internal override bool FlyingNavigation => true;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "bee";
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0 } && TagsRegistry.Item.Flowers.Entries.Contains(item.Holder.Id);
    protected override VectorD Travel() => VolumeMovement.Travel(this, !InWater);
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new MeleeAttackGoal(this, 1));
        actions.AddGoal(2, new BreedGoal(this));
        actions.AddGoal(3, new TemptGoal(this, CanEat));
        actions.AddGoal(4, new BeeWorkGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 1));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 6));
        targets.AddGoal(1, new HurtByTargetGoal(this));
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (AngerTicks > 0 && --AngerTicks == 0)
        {
            AttackTarget = null;
            SynchronizeMetadata();
        }
        if (HasStung && ++stingTicks >= 1200)
            await DamageEnvironmentAsync(Health);
        if (searchCooldown > 0) searchCooldown--;
        if (HivePosition is Vector hive && Terrain.GetBlock(hive)?.Material is not Material.BeeNest and not Material.Beehive)
            HivePosition = null;
        if (FlowerPosition is Vector flower && (Terrain.GetBlock(flower) is not { } plant || !TagsRegistry.Block.Flowers.Entries.Contains(plant.RegistryId)))
        {
            FlowerPosition = null;
            pollinationTicks = 0;
        }
        if (searchCooldown == 0 && (HivePosition == null || !HasNectar && FlowerPosition == null))
        {
            searchCooldown = 200;
            var origin = (Vector)Position.Floor();
            var candidates = new List<(Vector Position, double Distance)>();
            for (var x = -8; x <= 8; x++)
            for (var y = -5; y <= 5; y++)
            for (var z = -8; z <= 8; z++)
            {
                var point = new Vector(origin.X + x, origin.Y + y, origin.Z + z);
                if (Terrain.GetBlock(point) is not { } block) continue;
                if (HivePosition == null && block.Material is Material.BeeNest or Material.Beehive &&
                    GetHive(point) is DataBlockEntity entity &&
                    (!entity.Data.TryGetTag<NbtList>("bees", out var occupants) || occupants.Count < 3))
                    candidates.Add((point, ((VectorD)point - Position).MagnitudeSquared()));
                if (FlowerPosition == null && TagsRegistry.Block.Flowers.Entries.Contains(block.RegistryId))
                    FlowerPosition = point;
            }
            if (candidates.Count > 0) HivePosition = candidates.MinBy(candidate => candidate.Distance).Position;
        }
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (ReferenceEquals(source, this) || HasStung) return default;
        AngerTicks = Random.Next(400, 801);
        foreach (var bee in GetEntitiesNear(16).OfType<Bee>())
        {
            if (!bee.HasStung) { bee.AngerTicks = AngerTicks; bee.AlertedTarget = source; bee.SynchronizeMetadata(); }
        }
        SynchronizeMetadata();
        return default;
    }
    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        if (HasStung || AngerTicks <= 0) return;
        await base.PerformMeleeAttackAsync(target);
        if (target is Living living)
        {
            living.AbsorbedStingers++;
            var duration = Level.LevelData.Difficulty switch { Difficulty.Normal => 200, Difficulty.Hard => 360, _ => 0 };
            if (duration > 0) living.AddPotionEffect((int)PotionEffect.Poison - 1, duration);
        }
        HasStung = true;
        AngerTicks = 0;
        AttackTarget = null;
        PlayMobSound("sting");
        SynchronizeMetadata();
    }
    internal async ValueTask WorkAsync()
    {
        if (AngerTicks > 0 || HasStung) return;
        if (HivePosition is Vector hive && (HasNectar || Level.LevelData.Raining || Level.DayTime is >= 12000 and < 23000))
        {
            var point = (VectorD)hive + new VectorD(0.5f, 0.2f, 1.2f);
            ((Navigator)Navigator!).NavigateTo(point);
            if ((point - Position).MagnitudeSquared() < 2 && GetHive(hive) is DataBlockEntity entity)
            {
                lock (entity.Data)
                {
                    if (!entity.Data.TryGetTag<NbtList>("bees", out var occupants)) occupants = new NbtList(NbtTagType.Compound, "bees");
                    if (occupants.Count >= 3) return;
                    var data = EntityNbt.Save(this)!;
                    data.Name = "entity_data";
                    occupants.Add(new NbtCompound { data, new NbtTag<int>("ticks_in_hive", 0), new NbtTag<int>("min_ticks_in_hive", HasNectar ? 2400 : 600) });
                    entity.Set(occupants);
                }
                await RemoveAsync();
            }
        }
        else if (!HasNectar && !Level.LevelData.Raining && FlowerPosition is Vector flower)
        {
            var point = (VectorD)flower + new VectorD(0.5f, 0.6f, 0.5f);
            ((Navigator)Navigator!).NavigateTo(point);
            if ((point - Position).MagnitudeSquared() < 1 && ++pollinationTicks >= 400)
            {
                HasNectar = true;
                pollinationTicks = 0;
                SynchronizeMetadata();
                PlayMobSound("pollinate");
            }
        }
    }
    private DataBlockEntity? GetHive(Vector position) => Level is AbstractLevel level
        ? level.GetLoadedChunk(position.X >> 4, position.Z >> 4)?.GetBlockEntity(position.X, position.Y, position.Z) as DataBlockEntity
        : null;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        writer.WriteByte((byte)((HasNectar ? 8 : 0) | (HasStung ? 4 : 0) | (AttackTarget != null ? 2 : 0)));
        writer.WriteEntityMetadataType(18, EntityMetadataType.VarLong);
        writer.WriteVarLong(AngerTicks > 0 ? Level.LevelData.Time + AngerTicks : 0);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteBool("HasNectar", HasNectar);
        writer.WriteBool("HasStung", HasStung);
        writer.WriteInt("AngerTime", AngerTicks);
        writer.WriteInt("ObsidianStingTicks", stingTicks);
        if (HivePosition is Vector hive) writer.WriteArray("ObsidianHivePos", new[] { hive.X, hive.Y, hive.Z });
        if (FlowerPosition is Vector flower) writer.WriteArray("ObsidianFlowerPos", new[] { flower.X, flower.Y, flower.Z });
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        HasNectar = tag.TryGetBool("HasNectar", out var nectar) && nectar;
        HasStung = tag.TryGetBool("HasStung", out var stung) && stung;
        AngerTicks = Math.Clamp((tag.TryGetTagValue<int>("AngerTime", out var savedAngerTime) ? savedAngerTime : 0), 0, 800);
        stingTicks = Math.Clamp((tag.TryGetTagValue<int>("ObsidianStingTicks", out var savedObsidianStingTicks) ? savedObsidianStingTicks : 0), 0, 1200);
        if (tag.TryGetTag<NbtArray<int>>("ObsidianHivePos", out var hive) && hive.Count == 3) HivePosition = new Vector(hive[0], hive[1], hive[2]);
        if (tag.TryGetTag<NbtArray<int>>("ObsidianFlowerPos", out var flower) && flower.Count == 3) FlowerPosition = new Vector(flower[0], flower[1], flower[2]);
    }
}

internal sealed class BeeWorkGoal(Bee bee) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => bee.AngerTicks == 0 && !bee.HasStung && (bee.HivePosition != null &&
        (bee.HasNectar || bee.Level.LevelData.Raining || bee.Level.DayTime is >= 12000 and < 23000) || !bee.HasNectar && bee.FlowerPosition != null);
    public override ValueTask TickAsync() => bee.WorkAsync();
    public override void Stop() => ((Navigator)bee.Navigator!).Stop();
}
