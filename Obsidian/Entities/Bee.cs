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
    private bool hasNectar;
    internal int CropsGrownSincePollination { get; private set; }
    public Bee() => Type = EntityType.Bee;

    public bool HasNectar
    {
        get => hasNectar;
        internal set
        {
            if (hasNectar && !value)
                CropsGrownSincePollination = 0;
            hasNectar = value;
        }
    }

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
        actions.AddGoal(7, new BeeGrowCropGoal(this));
        actions.AddGoal(6, new RandomStrollGoal(this, 1));
        actions.AddGoal(7, new LookAtPlayerGoal(this, 6));
        targets.AddGoal(1, new HurtByTargetGoal(this));
    }

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi))
            return;
        if (AngerTicks > 0 && --AngerTicks == 0)
        {
            AttackTarget = null;
            SynchronizeMetadata();
        }
        if (HasStung && ++stingTicks >= 1200)
            await DamageEnvironmentAsync(Health);
        if (searchCooldown > 0)
            searchCooldown--;
        if (HivePosition is Vector hive
            && Terrain.GetBlock(hive)?.RegistryId is not BlockIds.BeeNest and not BlockIds.Beehive)
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
                        if (Terrain.GetBlock(point) is not { } block)
                            continue;
                if (HivePosition == null && block.RegistryId is BlockIds.BeeNest or BlockIds.Beehive &&
                    GetHive(point) is DataBlockEntity entity &&
                    (!entity.Data.TryGetTag<NbtList>("bees", out var occupants) || occupants.Count < 3))
                    candidates.Add((point, ((VectorD)point - Position).MagnitudeSquared()));
                if (FlowerPosition == null && TagsRegistry.Block.Flowers.Entries.Contains(block.RegistryId))
                    FlowerPosition = point;
            }
            if (candidates.Count > 0)
                HivePosition = candidates.MinBy(candidate => candidate.Distance).Position;
        }
    }

    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (ReferenceEquals(source, this) || HasStung)
            return default;
        AngerTicks = Random.Next(400, 801);
        foreach (var bee in GetEntitiesNear(16).OfType<Bee>())
        {
            if (!bee.HasStung)
            {
                bee.AngerTicks = AngerTicks;
                bee.AlertedTarget = source;
                bee.SynchronizeMetadata();
            }
        }
        SynchronizeMetadata();
        return default;
    }

    protected internal override async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        if (HasStung || AngerTicks <= 0)
            return;
        await base.PerformMeleeAttackAsync(target);
        if (target is Living living)
        {
            living.AbsorbedStingers++;
            var duration = Level.LevelData.Difficulty switch { Difficulty.Normal => 200, Difficulty.Hard => 360, _ => 0 };
            if (duration > 0)
                living.AddPotionEffect((int)PotionEffect.Poison - 1, duration);
        }
        HasStung = true;
        AngerTicks = 0;
        AttackTarget = null;
        PlayMobSound("sting");
        SynchronizeMetadata();
    }

    internal async ValueTask WorkAsync()
    {
        if (AngerTicks > 0 || HasStung)
            return;
        if (HivePosition is Vector hive && (HasNectar || Level.LevelData.Raining || Level.DayTime is >= 12000 and < 23000))
        {
            var point = (VectorD)hive + new VectorD(0.5f, 0.2f, 1.2f);
            ((Navigator)Navigator!).NavigateTo(point);
            if ((point - Position).MagnitudeSquared() < 2 && GetHive(hive) is DataBlockEntity entity)
            {
                lock (entity.Data)
                {
                    if (!entity.Data.TryGetTag<NbtList>("bees", out var occupants))
                        occupants = new NbtList(NbtTagType.Compound, "bees");
                    if (occupants.Count >= 3)
                        return;
                    API.Data.AttachmentStorage.For(this).Retire();
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

    internal bool HasValidHive => HivePosition is Vector hive &&
        ((VectorD)hive - Position.Floor()).MagnitudeSquared() < 48 * 48 &&
        Terrain.GetBlock(hive)?.RegistryId is BlockIds.BeeNest or BlockIds.Beehive &&
        GetHive(hive)?.Id is "minecraft:beehive" or "beehive";

    internal async ValueTask GrowCropsAsync()
    {
        var origin = (Vector)Position.Floor();
        for (var depth = 1; depth <= 2; depth++)
        {
            var point = origin - new Vector(0, depth, 0);
            if (Terrain.GetBlock(point) is not { } block)
                continue;
            var maxAge = block.RegistryId switch
            {
                BlockIds.Wheat or BlockIds.Carrots or BlockIds.Potatoes or BlockIds.MelonStem or BlockIds.PumpkinStem => 7,
                BlockIds.Beetroots or BlockIds.SweetBerryBush => 3,
                BlockIds.TorchflowerCrop => 2,
                _ => -1
            };
            IBlock? grown = null;
            if (maxAge >= 0 && int.TryParse(block.GetProperty("age"), out var age) && age < maxAge)
                grown = block.RegistryId == BlockIds.TorchflowerCrop && age + 1 == maxAge
                    ? BlocksRegistry.Torchflower
                    : block.WithProperty("age", age + 1);
            else if (block.RegistryId is BlockIds.CaveVines or BlockIds.CaveVinesPlant && block.GetProperty("berries") == "false")
                grown = block.WithProperty("berries", true);
            if (grown == null)
                continue;
            if (Level is AbstractLevel level)
                level.BroadcastLevelEvent(2011, point, 15);
            await Level.SetBlockAsync(point, grown, true);
            CropsGrownSincePollination++;
        }
    }

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
        writer.WriteInt("CropsGrownSincePollination", CropsGrownSincePollination);
        writer.WriteBool("HasStung", HasStung);
        writer.WriteInt("AngerTime", AngerTicks);
        writer.WriteInt("ObsidianStingTicks", stingTicks);
        if (HivePosition is Vector hive)
            writer.WriteArray("ObsidianHivePos", new[] { hive.X, hive.Y, hive.Z });
        if (FlowerPosition is Vector flower)
            writer.WriteArray("ObsidianFlowerPos", new[] { flower.X, flower.Y, flower.Z });
    }

    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        HasNectar = tag.TryGetBool("HasNectar", out var nectar) && nectar;
        CropsGrownSincePollination = tag.TryGetTagValue<int>("CropsGrownSincePollination", out var crops) ? crops : 0;
        HasStung = tag.TryGetBool("HasStung", out var stung) && stung;
        AngerTicks = Math.Clamp((tag.TryGetTagValue<int>("AngerTime", out var savedAngerTime) ? savedAngerTime : 0), 0, 800);
        stingTicks = Math.Clamp((tag.TryGetTagValue<int>("ObsidianStingTicks", out var savedObsidianStingTicks) ? savedObsidianStingTicks : 0), 0, 1200);
        if (tag.TryGetTag<NbtArray<int>>("ObsidianHivePos", out var hive) && hive.Count == 3)
            HivePosition = new Vector(hive[0], hive[1], hive[2]);
        if (tag.TryGetTag<NbtArray<int>>("ObsidianFlowerPos", out var flower) && flower.Count == 3)
            FlowerPosition = new Vector(flower[0], flower[1], flower[2]);
    }
}

internal sealed class BeeGrowCropGoal(Bee bee) : Goal
{
    public override GoalFlags Flags => GoalFlags.None;

    public override bool CanUse() => bee.CropsGrownSincePollination < 10 && bee.Random.NextSingle() >= 0.3f &&
        bee.HasNectar && bee.HasValidHive && bee.AngerTicks == 0;

    // This goal runs every other tick, matching vanilla's reduced tick delay of 30.
    public override ValueTask TickAsync() => bee.Random.Next(15) == 0 ? bee.GrowCropsAsync() : default;
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
