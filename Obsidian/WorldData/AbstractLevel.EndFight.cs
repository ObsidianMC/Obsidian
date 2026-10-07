using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private bool endFightInitialized;
    private bool dragonKilled;
    private bool dragonPreviouslyKilled;
    private Guid fightDragon;
    private Vector exitPortal = new(0, 64, 0);
    private int gatewaysCreated;
    private int resurrectionTicks = -1;
    private Guid[] resurrectionCrystals = [];
    private int ticksWithoutDragon;
    private readonly Dictionary<Guid, int> endPortalCooldowns = [];
    private bool HasEndFight => DimensionName == "minecraft:the_end" && Generator is not MobTestGenerator;

    internal async ValueTask TickEndFightAsync()
    {
        if (!HasEndFight) return;
        await TickEndPortalsAsync();
        if (!Players.Values.Any(player => player.GameMode != GameMode.Spectator &&
            (player.Position - new VectorD(0, 128, 0)).MagnitudeSquared() < 192 * 192)) return;
        var ready = true;
        for (var x = -8; x <= 8; x++)
        for (var z = -8; z <= 8; z++)
            if (GetLoadedChunk(x, z) is not { IsGenerated: true } && await GetChunkAsync(x, z) is not { IsGenerated: true }) ready = false;
        if (!ready) return;
        var entities = GetEntitiesInRange(new VectorD(0, 128, 0), 256).ToArray();
        if (!endFightInitialized)
        {
            var terrain = new MobTerrain(this);
            for (var y = 127; y >= MinY; y--)
                if (terrain.GetBlock(new Vector(0, y, 0)) is { IsAir: false } center)
                { exitPortal = new Vector(0, center.Material == Material.Bedrock ? y - 3 : y + 1, 0); break; }
            dragonKilled = terrain.GetBlock(exitPortal + new Vector(1, 0, 0))?.Material == Material.EndPortal;
            dragonPreviouslyKilled = dragonKilled;
            var existing = entities.OfType<EnderDragon>().FirstOrDefault();
            if (existing != null) fightDragon = existing.Uuid;
            if (!dragonKilled && existing == null)
            {
                foreach (var spike in EndSpikeFeature.GetSpikesForLevel(RandomState.ParseSeed(Seed)))
                    await RebuildEndSpikeAsync(spike, false);
                await CreateExitPortalAsync(false);
            }
            endFightInitialized = true;
        }
        if (!dragonKilled)
        {
            var dragon = entities.OfType<EnderDragon>().FirstOrDefault(item => item.Uuid == fightDragon);
            if (dragon == null && (fightDragon == Guid.Empty || ++ticksWithoutDragon >= 1200)) CreateFightDragon();
            else if (dragon != null) { ticksWithoutDragon = 0; dragon.PreviouslyKilled = dragonPreviouslyKilled; }
            return;
        }
        if (resurrectionTicks < 0)
        {
            List<Guid> crystals = [];
            foreach (var offset in new[] { new Vector(2, 1, 0), new Vector(-2, 1, 0), new Vector(0, 1, 2), new Vector(0, 1, -2) })
            {
                var position = exitPortal + offset;
                var found = entities.OfType<EndCrystal>().Where(item => item.Health > 0 &&
                    Math.Floor(item.Position.X) == position.X && Math.Floor(item.Position.Y) == position.Y && Math.Floor(item.Position.Z) == position.Z).ToArray();
                if (found.Length == 0) return;
                crystals.AddRange(found.Select(item => item.Uuid));
            }
            resurrectionCrystals = crystals.ToArray();
            resurrectionTicks = 0;
            await CreateExitPortalAsync(false);
        }
        var ritual = entities.OfType<EndCrystal>().Where(item => resurrectionCrystals.Contains(item.Uuid) && item.Health > 0).ToArray();
        if (ritual.Length != resurrectionCrystals.Length)
        {
            await AbortDragonResurrectionAsync();
            return;
        }
        var tick = resurrectionTicks++;
        if (tick is 1 or 50 or 51 or 52 or 95 or 96 or 97 or 98 or 99 or 100 || tick >= 581 && tick <= 600)
            PacketBroadcaster.QueuePacketToLevel(this, new LevelEventPacket(3001, new Vector(0, 128, 0), 0));
        if (tick == 0 || tick == 501)
            foreach (var crystal in ritual) crystal.SetBeam(new Vector(0, 128, 0));
        if (tick >= 101 && tick < 501)
        {
            var spike = EndSpikeFeature.GetSpikesForLevel(RandomState.ParseSeed(Seed))[(tick - 101) / 40];
            if ((tick - 101) % 40 == 0)
                foreach (var crystal in ritual) crystal.SetBeam(new Vector(spike.CenterX, spike.Height + 1, spike.CenterZ));
            if ((tick - 101) % 40 == 39) await RebuildEndSpikeAsync(spike, true);
        }
        if (tick < 601) return;
        resurrectionTicks = -1;
        resurrectionCrystals = [];
        foreach (var crystal in entities.OfType<EndCrystal>()) { crystal.Invulnerable = false; crystal.SetBeam(null); }
        foreach (var crystal in ritual) { await crystal.RemoveAsync(); await ExplodeAsync(crystal, 6, crystal); }
        dragonKilled = false;
        CreateFightDragon();
    }

    private async ValueTask TickEndPortalsAsync()
    {
        foreach (var id in endPortalCooldowns.Keys.ToArray())
            if (--endPortalCooldowns[id] <= 0) endPortalCooldowns.Remove(id);
        foreach (var player in Players.Values.OfType<Player>().ToArray())
        {
            if (player.GameMode == GameMode.Spectator || endPortalCooldowns.ContainsKey(player.Uuid)) continue;
            var position = (Vector)player.Position.Floor();
            var block = await GetBlockAsync(position);
            if (block?.Material == Material.EndPortal && this is IDimension dimension)
            {
                endPortalCooldowns[player.Uuid] = 100;
                await player.TransferDimensionAsync(dimension.ParentWorld);
            }
            else if (block?.Material == Material.EndGateway)
            {
                var data = await GetBlockEntityAsync(position) as DataBlockEntity;
                Vector destination;
                if (data != null && data.Data.TryGetTag<NbtArray<int>>("exit_portal", out var savedExit) && savedExit.Count == 3)
                {
                    var values = savedExit.GetArray();
                    destination = new Vector(values[0], values[1], values[2]);
                }
                else
                {
                    var length = Math.Sqrt(position.X * (double)position.X + position.Z * (double)position.Z);
                    if (length < 1) continue;
                    destination = new Vector((int)Math.Floor(position.X / length * 1024), 75, (int)Math.Floor(position.Z / length * 1024));
                    if (await GetChunkAsync(destination.X >> 4, destination.Z >> 4) is not { IsGenerated: true }) continue;
                    var terrain = new MobTerrain(this);
                    for (var y = MinY + Height - 1; y >= MinY; y--)
                        if (terrain.GetBlock(new Vector(destination.X, y, destination.Z)) is { IsAir: false })
                        { destination = new Vector(destination.X, y + 3, destination.Z); break; }
                    // Gateways always have a safe arrival platform, including gateways aimed into the void.
                    for (var x = -2; x <= 2; x++)
                    for (var z = -2; z <= 2; z++)
                        await SetBlockAsync(destination + new Vector(x, -2, z), BlocksRegistry.Get(Material.EndStone), true);
                    var returnGateway = destination + new Vector(0, 1, 0);
                    await CreateEndGatewayAsync(returnGateway);
                    await StoreGatewayExitAsync(returnGateway, position + new Vector(0, 3, 0));
                    await StoreGatewayExitAsync(position, destination + new Vector(2, -1, 0));
                    destination += new Vector(2, -1, 0);
                }
                if (await GetChunkAsync(destination.X >> 4, destination.Z >> 4) is not { IsGenerated: true }) continue;
                endPortalCooldowns[player.Uuid] = 100;
                await player.TeleportAsync(new VectorD(destination.X + 0.5, destination.Y, destination.Z + 0.5));
            }
        }
    }

    private async ValueTask StoreGatewayExitAsync(Vector gateway, Vector destination)
    {
        var data = await GetBlockEntityAsync(gateway) as DataBlockEntity;
        data ??= new DataBlockEntity { Id = "minecraft:end_gateway", BlockPosition = gateway };
        data.Set(new NbtArray<int>("exit_portal", [destination.X, destination.Y, destination.Z]));
        data.Set("ExactTeleport", true);
        await SetBlockEntity(gateway, data);
    }

    private void CreateFightDragon()
    {
        var dragon = new EnderDragon { Level = this, EntityId = Server.GetNextEntityId(),
            Position = new VectorD(0, 128, 0), PreviouslyKilled = dragonPreviouslyKilled };
        SpawnEntity(dragon);
        fightDragon = dragon.Uuid;
    }

    internal async ValueTask EndFightDragonKilledAsync(EnderDragon dragon)
    {
        if (!HasEndFight || dragon.Uuid != fightDragon || dragonKilled) return;
        dragonKilled = true;
        fightDragon = Guid.Empty;
        await CreateExitPortalAsync(true);
        if (!dragonPreviouslyKilled)
            await SetBlockAsync(exitPortal + new Vector(0, 4, 0), BlocksRegistry.Get(Material.DragonEgg), true);
        dragonPreviouslyKilled = true;
        if (gatewaysCreated >= 20) return;
        var gatewayOrder = FeatureHelpers.ShuffledCopy(Enumerable.Range(0, 20), new LegacyRandomSource(RandomState.ParseSeed(Seed)));
        var index = gatewayOrder[19 - gatewaysCreated++];
        var angle = 2 * Math.PI * index / 20;
        var gateway = new Vector((int)Math.Floor(96 * Math.Cos(angle)), 75, (int)Math.Floor(96 * Math.Sin(angle)));
        await CreateEndGatewayAsync(gateway);
    }

    private async ValueTask CreateEndGatewayAsync(Vector gateway)
    {
        for (var x = -1; x <= 1; x++)
        for (var y = -2; y <= 2; y++)
        for (var z = -1; z <= 1; z++)
        {
            var material = x == 0 && y == 0 && z == 0 ? Material.EndGateway :
                y != 0 && (Math.Abs(y) == 2 ? x == 0 && z == 0 : x == 0 || z == 0) ? Material.Bedrock : Material.Air;
            await SetBlockAsync(gateway + new Vector(x, y, z), BlocksRegistry.Get(material), true);
        }
        if (await GetBlockEntityAsync(gateway) == null)
            await SetBlockEntity(gateway, new DataBlockEntity { Id = "minecraft:end_gateway", BlockPosition = gateway });
        PacketBroadcaster.QueuePacketToLevel(this, new LevelEventPacket(3000, gateway, 0));
    }

    internal async ValueTask AbortDragonResurrectionAsync(EndCrystal? destroyed = null)
    {
        if (!HasEndFight || resurrectionTicks < 0) return;
        if (destroyed != null && !resurrectionCrystals.Contains(destroyed.Uuid)) return;
        resurrectionTicks = -1;
        resurrectionCrystals = [];
        foreach (var crystal in GetEntitiesInRange(new VectorD(0, 128, 0), 256).OfType<EndCrystal>())
        { crystal.Invulnerable = false; crystal.SetBeam(null); }
        await CreateExitPortalAsync(true);
    }

    private async ValueTask CreateExitPortalAsync(bool active)
    {
        for (var x = -4; x <= 4; x++)
        for (var z = -4; z <= 4; z++)
        {
            var distance = x * x + z * z;
            if (distance > 12) continue;
            for (var y = -1; y <= 3; y++)
            {
                var material = y == -1 ? distance <= 6 ? Material.Bedrock : Material.EndStone :
                    y == 0 ? distance > 6 ? Material.Bedrock : active ? Material.EndPortal : Material.Air : Material.Air;
                if (x == 0 && z == 0 && y >= 0) material = Material.Bedrock;
                await SetBlockAsync(exitPortal + new Vector(x, y, z), BlocksRegistry.Get(material), true);
            }
        }
        foreach (var (offset, facing) in new[] { (new Vector(1, 2, 0), "east"), (new Vector(-1, 2, 0), "west"),
            (new Vector(0, 2, 1), "south"), (new Vector(0, 2, -1), "north") })
            await SetBlockAsync(exitPortal + offset, BlocksRegistry.Get(Material.WallTorch).WithProperty("facing", facing), true);
    }

    private async ValueTask RebuildEndSpikeAsync(EndSpike spike, bool resurrecting)
    {
        foreach (var crystal in GetEntitiesInRange(new VectorD(spike.CenterX, spike.Height, spike.CenterZ), 12).OfType<EndCrystal>().ToArray())
            await crystal.RemoveAsync();
        if (resurrecting)
        {
            for (var x = -10; x <= 10; x++)
            for (var y = -10; y <= 10; y++)
            for (var z = -10; z <= 10; z++)
                await SetBlockAsync(new Vector(spike.CenterX + x, spike.Height + y, spike.CenterZ + z), BlocksRegistry.Air, true);
            var blast = new EndCrystal { Level = this, EntityId = Server.GetNextEntityId(),
                Position = new VectorD(spike.CenterX + 0.5, spike.Height, spike.CenterZ + 0.5) };
            await ExplodeAsync(blast, 5, blast);
        }
        var radius = spike.Radius;
        for (var x = -radius; x <= radius; x++)
        for (var z = -radius; z <= radius; z++)
        for (var y = MinY; y <= spike.Height + 10; y++)
        {
            var material = x * x + z * z <= radius * radius + 1 && y < spike.Height ? Material.Obsidian : Material.Air;
            if (material == Material.Air && y <= 65) continue;
            await SetBlockAsync(new Vector(spike.CenterX + x, y, spike.CenterZ + z), BlocksRegistry.Get(material), true);
        }
        if (spike.Guarded)
            for (var x = -2; x <= 2; x++)
            for (var z = -2; z <= 2; z++)
            for (var y = 0; y <= 3; y++)
                if (Math.Abs(x) == 2 || Math.Abs(z) == 2 || y == 3)
                    await SetBlockAsync(new Vector(spike.CenterX + x, spike.Height + y, spike.CenterZ + z),
                        BlocksRegistry.Get(Material.IronBars).WithProperty("north", (Math.Abs(x) == 2 || y == 3) && z != -2)
                        .WithProperty("south", (Math.Abs(x) == 2 || y == 3) && z != 2)
                        .WithProperty("west", (Math.Abs(z) == 2 || y == 3) && x != -2)
                        .WithProperty("east", (Math.Abs(z) == 2 || y == 3) && x != 2), true);
        await SetBlockAsync(new Vector(spike.CenterX, spike.Height, spike.CenterZ), BlocksRegistry.Get(Material.Bedrock), true);
        var crystalEntity = new EndCrystal { Level = this, EntityId = Server.GetNextEntityId(),
            Position = new VectorD(spike.CenterX + 0.5, spike.Height + 1, spike.CenterZ + 0.5), Invulnerable = resurrecting };
        if (resurrecting) crystalEntity.SetBeam(new Vector(0, 128, 0));
        SpawnEntity(crystalEntity);
    }

    internal void WriteEndFightNbt(INbtWriter writer)
    {
        if (!HasEndFight) return;
        writer.WriteCompoundStart("ObsidianEndFight");
        writer.WriteBool("Initialized", endFightInitialized);
        writer.WriteBool("DragonKilled", dragonKilled);
        writer.WriteBool("PreviouslyKilled", dragonPreviouslyKilled);
        writer.WriteString("Dragon", fightDragon.ToString());
        writer.WriteInt("PortalY", exitPortal.Y);
        writer.WriteInt("Gateways", gatewaysCreated);
        writer.WriteInt("RespawnTicks", resurrectionTicks);
        writer.WriteString("Crystals", string.Join(',', resurrectionCrystals));
        writer.EndCompound();
    }

    internal void ReadEndFightNbt(NbtCompound tag)
    {
        if (!tag.TryGetTag<NbtCompound>("ObsidianEndFight", out var fight)) return;
        endFightInitialized = fight.TryGetBool("Initialized", out var initialized) && initialized;
        dragonKilled = fight.TryGetBool("DragonKilled", out var killed) && killed;
        dragonPreviouslyKilled = fight.TryGetBool("PreviouslyKilled", out var previous) && previous;
        if (fight.TryGetTagValue<string>("Dragon", out var dragon)) Guid.TryParse(dragon, out fightDragon);
        if (fight.TryGetTagValue<int>("PortalY", out var y)) exitPortal = new Vector(0, Math.Clamp(y, MinY + 1, MinY + Height - 5), 0);
        if (fight.TryGetTagValue<int>("Gateways", out var gateways)) gatewaysCreated = Math.Clamp(gateways, 0, 20);
        if (fight.TryGetTagValue<int>("RespawnTicks", out var ticks)) resurrectionTicks = Math.Clamp(ticks, -1, 601);
        if (fight.TryGetTagValue<string>("Crystals", out var crystals))
            resurrectionCrystals = crystals.Split(',').Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToArray();
    }
}
