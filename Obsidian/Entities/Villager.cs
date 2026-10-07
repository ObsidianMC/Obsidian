using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:villager")]
public sealed partial class Villager : AgeableMob
{
    internal static readonly string[] Professions = ["none", "armorer", "butcher", "cartographer", "cleric", "farmer", "fisherman", "fletcher", "leatherworker", "librarian", "mason", "nitwit", "shepherd", "toolsmith", "weaponsmith"];
    internal static readonly string[] BiomeTypes = ["desert", "jungle", "plains", "savanna", "snow", "swamp", "taiga"];
    internal static readonly Dictionary<Material, int> Workstations = new()
    { [Material.BlastFurnace] = 1, [Material.Smoker] = 2, [Material.CartographyTable] = 3, [Material.BrewingStand] = 4,
        [Material.Composter] = 5, [Material.Barrel] = 6, [Material.FletchingTable] = 7, [Material.Cauldron] = 8,
        [Material.Lectern] = 9, [Material.Stonecutter] = 10, [Material.Loom] = 12, [Material.SmithingTable] = 13, [Material.Grindstone] = 14 };
    public int VillagerType { get; set; } = 2;
    public int VillagerProfession { get; set; }
    public int VillagerLevel { get; set; } = 1;
    public int VillagerXp { get; set; }
    internal Vector? Home { get; private set; }
    internal Vector? JobSite { get; private set; }
    private ImmutableArray<TradeEntry> offers = [];
    private int restocks;
    private long restockDay = -1;
    private long lastRestock;
    private int tradingLevel;
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
        if (!MobBitMask.HasFlag(MobBitmask.NoAi) && Level is AbstractLevel village && village.Generator is not Obsidian.WorldData.Generators.MobTestGenerator)
            await TickVillageAsync(village);
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

    internal override async ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (IsBaby || VillagerProfession is 0 or 11 || !CanSee(player)) return;
        EnsureTrades();
        await MerchantTrading.OpenAsync(this, player, offers, VillagerLevel, VillagerXp, true, offer =>
        {
            VillagerXp += offer.XP;
            var newLevel = VillagerXp >= 250 ? 5 : VillagerXp >= 150 ? 4 : VillagerXp >= 70 ? 3 : VillagerXp >= 10 ? 2 : 1;
            if (newLevel > VillagerLevel) { VillagerLevel = newLevel; Health = GetAttributeValue("minecraft:generic.max_health"); SynchronizeMetadata(); }
        });
    }

    private async ValueTask TickVillageAsync(AbstractLevel level)
    {
        if (AiTick % 200 == 0)
        {
            if (Home is { } home && (Terrain.GetBlock(home) is not { } bed || !TagsRegistry.Block.Beds.Entries.Contains(bed.RegistryId)))
            { level.ReleaseVillagePoi(home, Uuid); Home = null; }
            if (JobSite is { } job && (!Workstations.TryGetValue(Terrain.GetBlock(job)?.Material ?? Material.Air, out var profession) || profession != VillagerProfession))
            {
                level.ReleaseVillagePoi(job, Uuid); JobSite = null;
                if (VillagerXp == 0) { VillagerProfession = 0; offers = []; tradingLevel = 0; SynchronizeMetadata(); }
            }
            var origin = (Vector)Position.Floor();
            var points = new List<(Vector Position, int Profession, bool Bed)>();
            for (var x = -16; x <= 16; x++)
            for (var z = -16; z <= 16; z++)
            for (var y = -4; y <= 4; y++)
            {
                var point = origin + new Vector(x, y, z);
                if (Terrain.GetBlock(point) is not { } block) continue;
                if (Home == null && TagsRegistry.Block.Beds.Entries.Contains(block.RegistryId) && block.GetProperty("part") == "head") points.Add((point, 0, true));
                else if (!IsBaby && JobSite == null && VillagerProfession != 11 && Workstations.TryGetValue(block.Material, out var candidateProfession) &&
                    (VillagerProfession == 0 || VillagerProfession == candidateProfession)) points.Add((point, candidateProfession, false));
            }
            foreach (var point in points.OrderBy(point => ((VectorD)point.Position - Position).MagnitudeSquared()))
            {
                if (point.Bed ? Home != null : JobSite != null) continue;
                if (!level.TryClaimVillagePoi(point.Position, Uuid)) continue;
                if (point.Bed) Home = point.Position;
                else { JobSite = point.Position; VillagerProfession = point.Profession; EnsureTrades(); SynchronizeMetadata(); }
            }
        }
        var customer = Level.GetPlayersInRange(Position, 16).FirstOrDefault(player => player.OpenedContainer is MerchantContainer menu && ReferenceEquals(menu.Merchant, this));
        if (customer != null) { (Navigator as Navigator)?.Stop(); LookControl.LookAt(customer); return; }
        var nighttime = Level.DayTime is >= 12000 and < 23000;
        var target = nighttime ? Home : Level.DayTime is >= 2000 and < 9000 ? JobSite : null;
        if (AiTick % 40 == 0 && target is { } destination) Navigator?.NavigateTo((VectorD)destination + new VectorD(0.5, 0, 0.5));
        if (nighttime && Home is { } sleeping && ((VectorD)sleeping - Position).MagnitudeSquared() < 4)
        {
            (Navigator as Navigator)?.Stop(); Motion = VectorD.Zero; BedBlockPosition = sleeping;
            if (Pose != Pose.Sleeping) { Pose = Pose.Sleeping; SynchronizeMetadata(); }
        }
        else if (Pose == Pose.Sleeping) { Pose = Pose.Standing; BedBlockPosition = null; SynchronizeMetadata(); }
        var day = Level.Time / 24000;
        if (day != restockDay) { restockDay = day; restocks = 0; }
        if (!nighttime && Level.DayTime is >= 2000 and < 9000 && JobSite is { } work &&
            ((VectorD)work - Position).MagnitudeSquared() < 4 && restocks < 2 && Level.Time - lastRestock >= 2000 && offers.Any(offer => offer.UsedCount > 0))
        {
            foreach (var offer in offers) { offer.Demand += offer.UsedCount - (offer.MaxCount - offer.UsedCount); offer.UsedCount = 0; offer.IsDisabled = false; }
            restocks++; lastRestock = Level.Time;
        }
        await ValueTask.CompletedTask;
    }

    private void EnsureTrades()
    {
        if (VillagerProfession is <= 0 or 11 or > 14) return;
        while (tradingLevel < VillagerLevel)
        {
            tradingLevel++;
            offers = offers.AddRange(VillagerTrades.ForLevel(VillagerProfession, tradingLevel));
        }
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(18, EntityMetadataType.VillagerData);
        writer.WriteVarInt(VillagerType); writer.WriteVarInt(VillagerProfession); writer.WriteVarInt(VillagerLevel);
    }

    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteCompoundStart("VillagerData");
        writer.WriteString("type", "minecraft:" + BiomeTypes[Math.Clamp(VillagerType, 0, 6)]);
        writer.WriteString("profession", "minecraft:" + Professions[Math.Clamp(VillagerProfession, 0, 14)]);
        writer.WriteInt("level", VillagerLevel); writer.EndCompound();
        writer.WriteInt("Xp", VillagerXp); writer.WriteInt("ObsidianTradingLevel", tradingLevel);
        writer.WriteInt("RestocksToday", restocks); writer.WriteLong("LastRestock", lastRestock); writer.WriteLong("ObsidianRestockDay", restockDay);
        if (Home is { } home) writer.WriteArray("ObsidianHome", [home.X, home.Y, home.Z]);
        if (JobSite is { } job) writer.WriteArray("ObsidianJobSite", [job.X, job.Y, job.Z]);
        MerchantOffers.Write(writer, offers);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        if (tag.TryGetTag<NbtCompound>("VillagerData", out var data))
        {
            if (data.TryGetTagValue<string>("type", out var type)) VillagerType = Math.Max(0, Array.IndexOf(BiomeTypes, type.Replace("minecraft:", "")));
            if (data.TryGetTagValue<string>("profession", out var profession)) VillagerProfession = Math.Max(0, Array.IndexOf(Professions, profession.Replace("minecraft:", "")));
            if (data.TryGetTagValue<int>("level", out var level)) VillagerLevel = Math.Clamp(level, 1, 5);
        }
        VillagerXp = tag.TryGetTagValue<int>("Xp", out var xp) ? Math.Max(0, xp) : 0;
        offers = MerchantOffers.Read(tag);
        tradingLevel = tag.TryGetTagValue<int>("ObsidianTradingLevel", out var unlocked) ? Math.Clamp(unlocked, 0, 5) : offers.IsEmpty ? 0 : VillagerLevel;
        restocks = tag.TryGetTagValue<int>("RestocksToday", out var stock) ? Math.Clamp(stock, 0, 2) : 0;
        if (tag.TryGetTagValue<long>("LastRestock", out var last)) lastRestock = last;
        if (tag.TryGetTagValue<long>("ObsidianRestockDay", out var day)) restockDay = day;
        if (tag.TryGetTag<NbtArray<int>>("ObsidianHome", out var home) && home.Count == 3) { var values = home.GetArray(); Home = new Vector(values[0], values[1], values[2]); }
        if (tag.TryGetTag<NbtArray<int>>("ObsidianJobSite", out var job) && job.Count == 3) { var values = job.GetArray(); JobSite = new Vector(values[0], values[1], values[2]); }
        if (Level is AbstractLevel village) { if (Home is { } h) village.TryClaimVillagePoi(h, Uuid); if (JobSite is { } j) village.TryClaimVillagePoi(j, Uuid); }
    }
    internal void RestoreVillagerOffers(NbtCompound tag)
    {
        offers = MerchantOffers.Read(tag);
        if (!offers.IsEmpty) tradingLevel = VillagerLevel;
    }

    public override async ValueTask RemoveAsync()
    {
        if (Level is AbstractLevel village) { if (Home is { } h) village.ReleaseVillagePoi(h, Uuid); if (JobSite is { } j) village.ReleaseVillagePoi(j, Uuid); }
        foreach (var player in Level.GetPlayersInRange(Position, 16))
            if (player.OpenedContainer is MerchantContainer merchant && ReferenceEquals(merchant.Merchant, this)) await merchant.CloseAsync(player);
        await base.RemoveAsync();
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
