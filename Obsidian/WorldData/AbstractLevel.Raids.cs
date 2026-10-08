using Obsidian.API.Boss;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private sealed class VillageRaid
    {
        internal Guid Id = Guid.NewGuid();
        internal Vector Center;
        internal int Omen = 1;
        internal int Wave;
        internal int Cooldown = 300;
        internal int Age;
        internal int VictoryTicks;
        internal float TotalHealth;
        internal readonly HashSet<Guid> Raiders = [];
        internal readonly HashSet<Guid> Heroes = [];
        internal BossBar? Bar;
    }
    private readonly List<VillageRaid> raids = [];
    private readonly Dictionary<Guid, (Vector Center, int Ticks, int Omen)> pendingRaids = [];
    private int patrolCountdown = 12000;
    private int traderCountdown = 24000;
    private int traderChance = 25;
    private static readonly (EntityType Type, int[] Counts)[] raidWaves =
    [ (EntityType.Vindicator, [0, 0, 2, 0, 1, 4, 2, 5]), (EntityType.Evoker, [0, 0, 0, 0, 0, 1, 1, 2]),
      (EntityType.Pillager, [0, 4, 3, 3, 4, 4, 4, 2]), (EntityType.Witch, [0, 0, 0, 0, 3, 0, 0, 1]),
      (EntityType.Ravager, [0, 0, 0, 1, 0, 1, 0, 2]) ];

    internal async ValueTask TickRaidsAsync()
    {
        if (DimensionName != "minecraft:overworld" || Generator is Obsidian.WorldData.Generators.MobTestGenerator) return;
        TickWanderingTraderSpawner();
        TickPatrolSpawner();
        if (LevelData.Difficulty == Difficulty.Peaceful || !LevelData.GetBooleanRule("raids"))
        { foreach (var raid in raids) ClearRaidBar(raid); raids.Clear(); pendingRaids.Clear(); return; }
        foreach (var player in Players.Values.OfType<Player>())
        {
            var village = GetEntitiesInRange(player.Position, 64).OfType<Villager>().Where(villager => villager.Alive && villager.Home != null)
                .MinBy(villager => (villager.Position - player.Position).MagnitudeSquared());
            if (village != null && player.ActivePotionEffects.TryGetValue((int)PotionEffect.BadOmen - 1, out var omen))
            {
                player.RemovePotionEffect((int)PotionEffect.BadOmen - 1);
                player.AddPotionEffect((int)PotionEffect.RaidOmen - 1, 600, omen.EffectData.Amplifier);
                pendingRaids[player.Uuid] = ((Vector)village.Position.Floor(), 600, Math.Clamp(omen.EffectData.Amplifier + 1, 1, 5));
            }
            if (!pendingRaids.TryGetValue(player.Uuid, out var pending)) continue;
            if (!player.HasPotionEffect((int)PotionEffect.RaidOmen - 1) && pending.Ticks > 1) { pendingRaids.Remove(player.Uuid); continue; }
            if (pending.Ticks > 1) { pendingRaids[player.Uuid] = (pending.Center, pending.Ticks - 1, pending.Omen); continue; }
            pendingRaids.Remove(player.Uuid);
            player.RemovePotionEffect((int)PotionEffect.RaidOmen - 1);
            var existing = raids.FirstOrDefault(raid => ((VectorD)raid.Center - (VectorD)pending.Center).MagnitudeSquared() < 96 * 96);
            if (existing == null) raids.Add(new VillageRaid { Center = pending.Center, Omen = pending.Omen });
            else existing.Omen = Math.Min(5, existing.Omen + pending.Omen);
        }
        foreach (var raid in raids.ToArray())
        {
            var observers = GetPlayersInRange((VectorD)raid.Center, 96).Where(player => player.GameMode != GameMode.Spectator).ToArray();
            if (observers.Length == 0) { ClearRaidBar(raid); continue; }
            raid.Bar ??= new BossBar(PacketBroadcaster, ChatMessage.Simple("Raid"), 1, BossBarColor.Red, BossBarDivisionType.None, BossBarFlags.None);
            foreach (var id in raid.Bar.Players.Where(id => !observers.Any(player => player.EntityId == id)).ToArray()) raid.Bar.RemovePlayer(id);
            foreach (var player in observers) raid.Bar.AddPlayer(player.EntityId);
            if (++raid.Age > 48000 || !GetEntitiesInRange((VectorD)raid.Center, 96).OfType<Villager>().Any(villager => villager.Alive && villager.Home != null))
            { raid.Bar.UpdateTitle(ChatMessage.Simple("Raid - Defeat")); if (++raid.VictoryTicks > 600) { ClearRaidBar(raid); raids.Remove(raid); } continue; }
            var living = GetEntitiesInRange((VectorD)raid.Center, 128).OfType<Mob>().Where(mob => raid.Raiders.Contains(mob.Uuid) && mob.Alive && !mob.IsRemoved).ToArray();
            var health = living.Sum(mob => mob.Health);
            raid.Bar.UpdateHealth(raid.TotalHealth > 0 ? Math.Clamp(health / raid.TotalHealth, 0, 1) : 1 - raid.Cooldown / 300f);
            foreach (var mob in living.OfType<PathfinderMob>())
                if (mob.AttackTarget == null && mob.Navigator is { } navigation && !navigation.IsNavigating) navigation.NavigateTo((VectorD)raid.Center);
            if (living.Length != 0) continue;
            var waves = LevelData.Difficulty == Difficulty.Easy ? 3 : LevelData.Difficulty == Difficulty.Normal ? 5 : 7;
            if (raid.Wave >= waves + (raid.Omen > 1 ? 1 : 0))
            {
                raid.Bar.UpdateTitle(ChatMessage.Simple("Raid - Victory"));
                if (++raid.VictoryTicks == 40)
                    foreach (var hero in Players.Values.OfType<Player>().Where(player => raid.Heroes.Contains(player.Uuid)))
                        hero.AddPotionEffect((int)PotionEffect.HeroOfTheVillage - 1, 48000, raid.Omen - 1);
                if (raid.VictoryTicks > 600) { ClearRaidBar(raid); raids.Remove(raid); }
                continue;
            }
            if (--raid.Cooldown > 0) continue;
            var spawn = FindRaidSpawn(raid.Center, 64);
            if (spawn == null) { raid.Cooldown = 20; continue; }
            raid.Wave++; raid.Raiders.Clear(); raid.TotalHealth = 0; raid.Cooldown = 300;
            var captain = false;
            foreach (var (type, counts) in raidWaves)
            {
                var count = counts[Math.Min(raid.Wave, waves)];
                if (type is EntityType.Pillager or EntityType.Vindicator) count += Random.Shared.Next(LevelData.Difficulty == Difficulty.Hard ? 3 : 2);
                if (type == EntityType.Witch && LevelData.Difficulty != Difficulty.Easy && raid.Wave > 2 && raid.Wave != 4) count += Random.Shared.Next(2);
                for (var i = 0; i < count; i++)
                {
                    var mob = EntitySpawner.CreateMob(this, type)!;
                    mob.EntityId = Server.GetNextEntityId(); mob.Position = spawn.Value + new VectorD(Random.Shared.NextDouble(), 0, Random.Shared.NextDouble());
                    mob.PersistenceRequired = true;
                    mob.UnmodeledData.Set(new NbtTag<string>("ObsidianRaid", raid.Id.ToString()));
                    if (!captain && type is EntityType.Pillager or EntityType.Vindicator or EntityType.Evoker)
                    { SetPatrolCaptain(mob); captain = true; }
                    SpawnEntity(mob); raid.Raiders.Add(mob.Uuid); raid.TotalHealth += mob.Health;
                }
            }
        }
        await ValueTask.CompletedTask;
    }

    private void TickPatrolSpawner()
    {
        if (LevelData.Difficulty == Difficulty.Peaceful || !LevelData.GetBooleanRule("spawn_patrols") || !LevelData.GetBooleanRule("spawn_mobs") || --patrolCountdown > 0) return;
        patrolCountdown = 12000 + Random.Shared.Next(1200);
        if (Time < 120000 || DayTime >= 12000 || Random.Shared.Next(5) != 0) return;
        var candidates = Players.Values.Where(player => player.GameMode != GameMode.Spectator).ToArray();
        if (candidates.Length == 0) return;
        var target = candidates[Random.Shared.Next(candidates.Length)];
        if (GetEntitiesInRange(target.Position, 48).OfType<Villager>().Any(villager => villager.Home != null)) return;
        var origin = FindRaidSpawn((Vector)target.Position.Floor(), 24 + Random.Shared.Next(24));
        if (origin == null) return;
        for (var i = 0; i < 2 + (int)LevelData.Difficulty; i++)
        {
            var mob = EntitySpawner.CreateMob(this, EntityType.Pillager)!;
            mob.EntityId = Server.GetNextEntityId(); mob.Position = origin.Value + new VectorD(i, 0, 0);
            mob.PersistenceRequired = true;
            mob.UnmodeledData.Set(new NbtTag<bool>("Patrolling", true));
            if (i == 0) SetPatrolCaptain(mob);
            SpawnEntity(mob);
            mob.Navigator?.NavigateTo(target.Position);
        }
    }

    private void TickWanderingTraderSpawner()
    {
        if (!LevelData.GetBooleanRule("spawn_wandering_traders") || !LevelData.GetBooleanRule("spawn_mobs") || --traderCountdown > 0) return;
        traderCountdown = 24000;
        var chance = traderChance; traderChance = Math.Min(75, traderChance + 25);
        var players = Players.Values.Where(player => player.GameMode != GameMode.Spectator).ToArray();
        if (players.Length == 0 || Random.Shared.Next(100) >= chance || Random.Shared.Next(10) != 0) return;
        var player = players[Random.Shared.Next(players.Length)];
        if (GetEntitiesInRange(player.Position, 96).OfType<WanderingTrader>().Any()) return;
        var origin = FindRaidSpawn((Vector)player.Position.Floor(), 48);
        if (origin == null) return;
        var trader = new WanderingTrader { Level = this, EntityId = Server.GetNextEntityId(), Position = origin.Value,
            WanderTarget = (Vector)player.Position.Floor(), DespawnDelay = 48000 };
        SpawnEntity(trader); traderChance = 25;
        for (var i = 0; i < 2; i++)
        {
            var spawn = FindRaidSpawn((Vector)origin.Value.Floor(), 4);
            if (spawn == null) continue;
            SpawnEntity(new TraderLlama { Level = this, EntityId = Server.GetNextEntityId(), Position = spawn.Value, TraderUuid = trader.Uuid });
        }
    }

    private VectorD? FindRaidSpawn(Vector center, int radius)
    {
        var terrain = new MobTerrain(this);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            var x = center.X + (int)(Math.Cos(angle) * radius); var z = center.Z + (int)(Math.Sin(angle) * radius);
            for (var y = Math.Min(MinY + Height - 2, center.Y + 32); y >= Math.Max(MinY, center.Y - 32); y--)
                if (terrain.GetBlock(new Vector(x, y, z)) is { IsAir: false } ground && ground.Material != Material.Water && ground.Material != Material.Lava &&
                    terrain.GetBlock(new Vector(x, y + 1, z))?.IsAir == true && terrain.GetBlock(new Vector(x, y + 2, z))?.IsAir == true)
                    return new VectorD(x + 0.5, y + 1, z + 0.5);
        }
        return null;
    }

    private static void SetPatrolCaptain(Mob mob)
    {
        mob.UnmodeledData.Set(new NbtTag<bool>("PatrolLeader", true));
        mob.SetEquipment(EquipmentSlot.Helmet, ItemsRegistry.GetSingleItem(Material.WhiteBanner));
    }
    internal void NotifyRaiderKilled(Mob raider, IEntity source)
    {
        var raid = raids.FirstOrDefault(candidate => candidate.Raiders.Remove(raider.Uuid));
        if (raid != null) { if (source is IPlayer) raid.Heroes.Add(source.Uuid); return; }
        if (LevelData.GetBooleanRule("mob_drops") && source is IPlayer && raider.UnmodeledData.TryGetBool("PatrolLeader", out var captain) && captain)
            SpawnEntity(new ItemEntity { Level = this, EntityId = Server.GetNextEntityId(), Position = raider.Position,
                Item = ItemsRegistry.GetSingleItem(Material.OminousBottle) });
    }
    private static void ClearRaidBar(VillageRaid raid)
    { if (raid.Bar != null) foreach (var id in raid.Bar.Players.ToArray()) raid.Bar.RemovePlayer(id); }

    internal void WriteRaidsNbt(INbtWriter writer)
    {
        writer.WriteInt("ObsidianPatrolCountdown", patrolCountdown);
        writer.WriteInt("ObsidianTraderCountdown", traderCountdown); writer.WriteInt("ObsidianTraderChance", traderChance);
        writer.WriteListStart("ObsidianRaids", NbtTagType.Compound, raids.Count);
        foreach (var raid in raids)
        {
            writer.WriteCompoundStart(); writer.WriteString("Id", raid.Id.ToString()); writer.WriteArray("Center", [raid.Center.X, raid.Center.Y, raid.Center.Z]);
            writer.WriteInt("Omen", raid.Omen); writer.WriteInt("Wave", raid.Wave); writer.WriteInt("Cooldown", raid.Cooldown);
            writer.WriteInt("Age", raid.Age); writer.WriteInt("Victory", raid.VictoryTicks); writer.WriteFloat("Health", raid.TotalHealth);
            writer.WriteString("Raiders", string.Join(',', raid.Raiders)); writer.WriteString("Heroes", string.Join(',', raid.Heroes)); writer.EndCompound();
        }
        writer.EndList();
    }
    internal void ReadRaidsNbt(NbtCompound tag)
    {
        if (tag.TryGetTagValue<int>("ObsidianPatrolCountdown", out var patrol)) patrolCountdown = Math.Clamp(patrol, 1, 13200);
        if (tag.TryGetTagValue<int>("ObsidianTraderCountdown", out var trader)) traderCountdown = Math.Clamp(trader, 1, 24000);
        if (tag.TryGetTagValue<int>("ObsidianTraderChance", out var chance)) traderChance = Math.Clamp(chance, 25, 75);
        if (!tag.TryGetTag<NbtList>("ObsidianRaids", out var saved)) return;
        foreach (var data in saved.OfType<NbtCompound>())
        {
            if (!data.TryGetTag<NbtArray<int>>("Center", out var center) || center.Count != 3) continue;
            var point = center.GetArray(); var raid = new VillageRaid { Center = new Vector(point[0], point[1], point[2]),
                Omen = Math.Clamp(data.GetInt("Omen"), 1, 5), Wave = Math.Clamp(data.GetInt("Wave"), 0, 8),
                Cooldown = Math.Clamp(data.GetInt("Cooldown"), 0, 300), Age = Math.Clamp(data.GetInt("Age"), 0, 48000),
                VictoryTicks = Math.Clamp(data.GetInt("Victory"), 0, 600), TotalHealth = data.TryGetTagValue<float>("Health", out var health) ? Math.Max(0, health) : 0 };
            if (data.TryGetTagValue<string>("Id", out var id) && Guid.TryParse(id, out var uuid)) raid.Id = uuid;
            foreach (var (name, set) in new[] { ("Raiders", raid.Raiders), ("Heroes", raid.Heroes) })
                if (data.TryGetTagValue<string>(name, out var ids)) foreach (var value in ids.Split(',')) if (Guid.TryParse(value, out var parsed)) set.Add(parsed);
            raids.Add(raid);
        }
    }
}
