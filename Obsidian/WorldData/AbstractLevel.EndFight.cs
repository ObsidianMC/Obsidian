using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    // Whether the fight has its portal's origin: from the first scan, or saved by vanilla.
    private bool exitPortalKnown;
    private int gatewaysCreated;
    private int resurrectionTicks = -1;
    private Guid[] resurrectionCrystals = [];
    private int ticksWithoutDragon;
    private readonly Dictionary<Guid, int> endPortalCooldowns = [];

    // Vanilla TheEndGatewayBlockEntity's 40-tick cooldown after it sends a non-player entity, keyed by gateway.
    private const int GatewayCooldown = 40;
    private readonly Dictionary<Vector, int> gatewayCooldowns = [];

    /// <summary>
    /// Starts the gateway's cooldown and returns true, or returns false while it's still cooling down from the last
    /// entity it sent.
    /// </summary>
    internal bool TryUseEndGateway(Vector gateway)
    {
        if (!gatewayCooldowns.TryAdd(gateway, GatewayCooldown))
            return false;

        this.QueueBlockEvent(gateway, BlockIds.EndGateway, 1, 0);
        return true;
    }

    private bool HasEndFight => DimensionName == "minecraft:the_end" && Generator is not MobTestGenerator;

    internal async ValueTask TickEndFightAsync()
    {
        TickGatewayCooldowns();
        if (!HasEndFight)
            return;

        await TickEndPortalsAsync();
        if (!AnyPlayerNearEndIsland())
            return;

        var ready = await LoadEndIslandChunksAsync();
        if (!ready)
            return;

        var entities = GetEntitiesInRange(new VectorD(0, 128, 0), 256).ToArray();
        if (!endFightInitialized)
            InitializeEndFight(entities);

        if (!dragonKilled)
        {
            TrackFightDragon(entities);
            return;
        }

        await TickDragonResurrectionAsync(entities);
    }

    private void TickGatewayCooldowns()
    {
        foreach (var gateway in gatewayCooldowns.Keys.ToArray())
        {
            if (--gatewayCooldowns[gateway] <= 0)
                gatewayCooldowns.Remove(gateway);
        }
    }

    private bool AnyPlayerNearEndIsland() => Players.Values.Any(player =>
        player.GameMode != GameMode.Spectator
        && (player.Position - new VectorD(0, 128, 0)).MagnitudeSquared() < 192 * 192);

    // Loads every chunk within 8 of the island's center, and returns whether all of them are generated.
    private async ValueTask<bool> LoadEndIslandChunksAsync()
    {
        var ready = true;
        for (var x = -8; x <= 8; x++)
        {
            for (var z = -8; z <= 8; z++)
            {
                if (GetLoadedChunk(x, z) is { IsGenerated: true })
                    continue;

                if (await GetChunkAsync(x, z) is not { IsGenerated: true })
                    ready = false;
            }
        }

        return ready;
    }

    // Like vanilla's first scan of a fight: any end portal near the island means the dragon was killed before, and
    // with one the fight is over unless a dragon is still alive. The spikes and crystals decoration placed stay (only
    // a resurrection rebuilds them), and an inactive exit portal is built only where no fountain stands yet.
    private void InitializeEndFight(IEntity[] entities)
    {
        var portals = EndPortalsNearIsland().ToArray();
        var activePortal = portals.Length > 0;
        var found = LocateExitPortal(portals);

        var existing = entities.OfType<EnderDragon>().FirstOrDefault();
        if (existing != null)
            fightDragon = existing.Uuid;

        dragonPreviouslyKilled = activePortal;
        dragonKilled = activePortal && existing == null;

        if (!activePortal && found is null)
            CreateExitPortal(false);

        endFightInitialized = true;
    }

    // The end portal block entities of the island's chunks, by chunk x and then z.
    private IEnumerable<Vector> EndPortalsNearIsland()
    {
        for (var x = -8; x <= 8; x++)
        {
            for (var z = -8; z <= 8; z++)
            {
                if (GetLoadedChunk(x, z) is not Chunk chunk)
                    continue;

                foreach (var blockEntity in chunk.BlockEntities.Values)
                {
                    if (blockEntity.Id == "minecraft:end_portal")
                        yield return blockEntity.BlockPosition;
                }
            }
        }
    }

    /// <summary>
    /// Finds the fountain of an existing exit portal (see <see cref="ExitPortalSearch.Find"/>). Unless the fight
    /// already knows where its portal is, the portal is the found one's, or else where a new one is to be built.
    /// </summary>
    /// <returns>The found fountain's origin, or null.</returns>
    private Vector? LocateExitPortal(Vector[] portals)
    {
        var terrain = new MobTerrain(this);
        var center = this.GetLoadedChunk(0, 0);
        var blocking = center?.GetHeight(HeightmapType.MotionBlocking, 0, 0) ?? this.MinY;
        var found = ExitPortalSearch.Find(terrain.GetBlock, portals, blocking, MinY);

        if (!exitPortalKnown)
        {
            var surface = center?.GetHeight(HeightmapType.MotionBlockingNoLeaves, 0, 0) ?? this.MinY;
            exitPortal = found ?? ExitPortalSearch.SpawnOrigin(terrain.GetBlock, surface, MinY);
            exitPortalKnown = true;
        }

        return found;
    }

    // Keeps the fight's dragon attached, and spawns a new one when it's been missing for a minute.
    private void TrackFightDragon(IEntity[] entities)
    {
        var dragon = entities.OfType<EnderDragon>().FirstOrDefault(item => item.Uuid == fightDragon);
        if (dragon == null && (fightDragon == Guid.Empty || ++ticksWithoutDragon >= 1200))
        {
            CreateFightDragon();
        }
        else if (dragon != null)
        {
            ticksWithoutDragon = 0;
            dragon.AttachFight(exitPortal, dragonPreviouslyKilled);
        }
    }

    private async ValueTask TickDragonResurrectionAsync(IEntity[] entities)
    {
        if (resurrectionTicks < 0)
        {
            var started = TryStartDragonResurrection(entities);
            if (!started)
                return;
        }

        var ritual = entities.OfType<EndCrystal>()
            .Where(item => resurrectionCrystals.Contains(item.Uuid) && item.Health > 0)
            .ToArray();
        if (ritual.Length != resurrectionCrystals.Length)
        {
            AbortDragonResurrection();
            return;
        }

        var tick = resurrectionTicks++;
        await PlayResurrectionStepAsync(tick, ritual);
        if (tick < 601)
            return;

        await FinishDragonResurrectionAsync(entities, ritual);
    }

    // The ritual starts once a living crystal stands on each side of the exit portal.
    private bool TryStartDragonResurrection(IEntity[] entities)
    {
        Vector[] sides = [new(2, 1, 0), new(-2, 1, 0), new(0, 1, 2), new(0, 1, -2)];
        List<Guid> crystals = [];
        foreach (var offset in sides)
        {
            var position = exitPortal + offset;
            var found = entities.OfType<EndCrystal>()
                .Where(item => item.Health > 0 && IsInBlock(item, position))
                .ToArray();
            if (found.Length == 0)
                return false;

            crystals.AddRange(found.Select(item => item.Uuid));
        }

        resurrectionCrystals = crystals.ToArray();
        resurrectionTicks = 0;
        CreateExitPortal(false);
        return true;
    }

    private static bool IsInBlock(EndCrystal crystal, Vector block) =>
        Math.Floor(crystal.Position.X) == block.X
        && Math.Floor(crystal.Position.Y) == block.Y
        && Math.Floor(crystal.Position.Z) == block.Z;

    // Vanilla's DragonRespawnAnimation: sounds and beams, then the spikes rebuilt one every 40 ticks.
    private async ValueTask PlayResurrectionStepAsync(int tick, EndCrystal[] ritual)
    {
        var center = new Vector(0, 128, 0);
        var playsSound = tick is 1 or 50 or 51 or 52 or 95 or 96 or 97 or 98 or 99 or 100
            || tick >= 581 && tick <= 600;
        if (playsSound)
            PacketBroadcaster.QueuePacketToLevel(this, new LevelEventPacket(3001, center, 0));

        if (tick == 0 || tick == 501)
        {
            foreach (var crystal in ritual)
                crystal.SetBeam(center);
        }

        if (tick >= 101 && tick < 501)
            await RebuildResurrectionSpikeAsync(tick - 101, ritual);
    }

    private async ValueTask RebuildResurrectionSpikeAsync(int spikeTick, EndCrystal[] ritual)
    {
        var spike = EndSpikeFeature.GetSpikesForLevel(RandomState.ParseSeed(Seed))[spikeTick / 40];
        if (spikeTick % 40 == 0)
        {
            var spikeTop = new Vector(spike.CenterX, spike.Height + 1, spike.CenterZ);
            foreach (var crystal in ritual)
                crystal.SetBeam(spikeTop);
        }

        if (spikeTick % 40 == 39)
            await RebuildEndSpikeAsync(spike);
    }

    private async ValueTask FinishDragonResurrectionAsync(IEntity[] entities, EndCrystal[] ritual)
    {
        resurrectionTicks = -1;
        resurrectionCrystals = [];
        foreach (var crystal in entities.OfType<EndCrystal>())
        {
            crystal.Invulnerable = false;
            crystal.SetBeam(null);
        }

        // Like vanilla's DragonRespawnAnimation, the ritual's crystals explode without breaking blocks.
        foreach (var crystal in ritual)
        {
            await crystal.RemoveAsync();
            await ExplodeAsync(crystal, 6, crystal, ExplosionInteraction.None);
        }

        dragonKilled = false;
        CreateFightDragon();
    }

    private async ValueTask TickEndPortalsAsync()
    {
        foreach (var id in endPortalCooldowns.Keys.ToArray())
        {
            if (--endPortalCooldowns[id] <= 0)
                endPortalCooldowns.Remove(id);
        }

        foreach (var player in Players.Values.ToArray())
        {
            if (player.GameMode == GameMode.Spectator || endPortalCooldowns.ContainsKey(player.Uuid))
                continue;

            var position = (Vector)player.Position.Floor();
            var block = await GetBlockAsync(position);
            if (block?.RegistryId == BlockIds.EndGateway)
            {
                if (await FindEndGatewayExitAsync(position) is not Vector destination)
                    continue;

                this.QueueBlockEvent(position, BlockIds.EndGateway, 1, 0);
                endPortalCooldowns[player.Uuid] = 100;
                await player.TeleportAsync(new VectorD(destination.X + 0.5, destination.Y, destination.Z + 0.5));
            }
        }
    }

    /// <summary>
    /// The block an entity entering the End gateway at <paramref name="gateway"/> arrives in, building the far
    /// gateway and its platform the first time; null while there is no exit or its chunk isn't generated.
    /// </summary>
    internal async ValueTask<Vector?> FindEndGatewayExitAsync(Vector gateway)
    {
        var data = await GetBlockEntityAsync(gateway) as DataBlockEntity;
        Vector destination;
        if (data != null
            && data.Data.TryGetTag<NbtArray<int>>("exit_portal", out var savedExit)
            && savedExit.Count == 3)
        {
            var values = savedExit.GetArray();
            destination = new Vector(values[0], values[1], values[2]);
        }
        else if (await BuildEndGatewayExitAsync(gateway) is Vector built)
        {
            destination = built;
        }
        else
        {
            return null;
        }

        if (await GetChunkAsync(destination.X >> 4, destination.Z >> 4) is not { IsGenerated: true })
            return null;

        return destination;
    }

    /// <summary>
    /// Builds the far gateway 1024 blocks out from <paramref name="gateway"/>, on an End stone platform, links the two
    /// and returns where an entity arrives; null for the gateway at the centre or when the far chunk isn't generated.
    /// </summary>
    private async ValueTask<Vector?> BuildEndGatewayExitAsync(Vector gateway)
    {
        var length = Math.Sqrt(gateway.X * (double)gateway.X + gateway.Z * (double)gateway.Z);
        if (length < 1)
            return null;

        var destination = new Vector(
            (int)Math.Floor(gateway.X / length * 1024),
            75,
            (int)Math.Floor(gateway.Z / length * 1024));
        if (await GetChunkAsync(destination.X >> 4, destination.Z >> 4) is not { IsGenerated: true })
            return null;

        destination = FindGatewayExitSurface(destination);
        // Gateways always have a safe arrival platform, including gateways aimed into the void.
        for (var x = -2; x <= 2; x++)
        {
            for (var z = -2; z <= 2; z++)
                await SetBlockAsync(destination + new Vector(x, -2, z), BlocksRegistry.EndStone, true);
        }

        var returnGateway = destination + new Vector(0, 1, 0);
        await CreateEndGatewayAsync(returnGateway);
        await StoreGatewayExitAsync(returnGateway, gateway + new Vector(0, 3, 0));
        await StoreGatewayExitAsync(gateway, destination + new Vector(2, -1, 0));
        return destination + new Vector(2, -1, 0);
    }

    /// <summary>Three blocks above the highest block in <paramref name="destination"/>'s column, if any.</summary>
    private Vector FindGatewayExitSurface(Vector destination)
    {
        var terrain = new MobTerrain(this);
        for (var y = MinY + Height - 1; y >= MinY; y--)
        {
            if (terrain.GetBlock(new Vector(destination.X, y, destination.Z)) is { IsAir: false })
                return new Vector(destination.X, y + 3, destination.Z);
        }

        return destination;
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
        var dragon = new EnderDragon
        {
            Level = this,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(0, 128, 0),
            PreviouslyKilled = dragonPreviouslyKilled
        };
        dragon.BeginFight(exitPortal);
        SpawnEntity(dragon);
        fightDragon = dragon.Uuid;
    }

    internal async ValueTask EndFightDragonKilledAsync(EnderDragon dragon)
    {
        if (!HasEndFight || dragon.Uuid != fightDragon || dragonKilled)
            return;

        dragonKilled = true;
        fightDragon = Guid.Empty;
        CreateExitPortal(true);
        if (!dragonPreviouslyKilled)
            await SetBlockAsync(exitPortal + new Vector(0, 4, 0), BlocksRegistry.DragonEgg, true);

        dragonPreviouslyKilled = true;
        if (gatewaysCreated >= 20)
            return;

        var gatewayOrder = ShuffledGatewayOrder();
        var index = gatewayOrder[19 - gatewaysCreated++];
        var angle = 2 * Math.PI * index / 20;
        var gateway = new Vector((int)Math.Floor(96 * Math.Cos(angle)), 75, (int)Math.Floor(96 * Math.Sin(angle)));
        await CreateEndGatewayAsync(gateway);
    }

    private async ValueTask CreateEndGatewayAsync(Vector gateway)
    {
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                for (var z = -1; z <= 1; z++)
                    await SetBlockAsync(gateway + new Vector(x, y, z), GatewayFrameBlock(x, y, z), true);
            }
        }

        if (await GetBlockEntityAsync(gateway) == null)
        {
            var data = new DataBlockEntity { Id = "minecraft:end_gateway", BlockPosition = gateway };
            await SetBlockEntity(gateway, data);
        }

        PacketBroadcaster.QueuePacketToLevel(this, new LevelEventPacket(3000, gateway, 0));
    }

    /// <summary>
    /// The block at an offset from a gateway: the gateway in the middle, bedrock in the cross around it above and
    /// below and in the single blocks capping it, air elsewhere.
    /// </summary>
    private static IBlock GatewayFrameBlock(int x, int y, int z)
    {
        if (x == 0 && y == 0 && z == 0)
            return BlocksRegistry.EndGateway;

        if (y == 0)
            return BlocksRegistry.Air;

        var frame = Math.Abs(y) == 2 ? x == 0 && z == 0 : x == 0 || z == 0;
        return frame ? BlocksRegistry.Bedrock : BlocksRegistry.Air;
    }

    internal void AbortDragonResurrection(EndCrystal? destroyed = null)
    {
        if (!HasEndFight || resurrectionTicks < 0)
            return;

        if (destroyed != null && !resurrectionCrystals.Contains(destroyed.Uuid))
            return;

        resurrectionTicks = -1;
        resurrectionCrystals = [];
        foreach (var crystal in GetEntitiesInRange(new VectorD(0, 128, 0), 256).OfType<EndCrystal>())
        {
            crystal.Invulnerable = false;
            crystal.SetBeam(null);
        }

        CreateExitPortal(true);
    }

    // The 26.3 end_podium feature, inactive and active, which vanilla's fight places its exit portal with.
    private static readonly Lazy<ExitPortalFeatures> exitPortalFeatures = new(() =>
    {
        var registry = new FeatureRegistry(WorldgenData.Vanilla);

        return new ExitPortalFeatures(registry, ExitPortalFeature(registry, false), ExitPortalFeature(registry, true));
    });

    private sealed record ExitPortalFeatures(FeatureRegistry Registry, Feature Inactive, Feature Active);

    private static Feature ExitPortalFeature(FeatureRegistry registry, bool active)
    {
        var definition = new JsonObject
        {
            ["type"] = "minecraft:end_podium",
            ["active"] = active
        };

        return registry.Feature(JsonSerializer.SerializeToElement(definition));
    }

    private void CreateExitPortal(bool active)
    {
        var features = exitPortalFeatures.Value;
        var podium = active ? features.Active : features.Inactive;

        lock (Fluids.TickLock)
        {
            var view = new LiveFeatureLevel(this);
            var context = new DecorationContext(features.Registry, view, at => view.GetBiome(at).Name, MinY, Height);

            podium(context, view.Random, exitPortal);
            view.Commit();
        }
    }

    // The order vanilla places the 20 gateways in, seeded by the world.
    private List<int> ShuffledGatewayOrder()
    {
        var random = new LegacyRandomSource(RandomState.ParseSeed(Seed));
        return FeatureHelpers.ShuffledCopy(Enumerable.Range(0, 20), random);
    }

    // A resurrection's rebuild of one spike: its top blasted away, then the pillar, cage and an invulnerable crystal.
    private async ValueTask RebuildEndSpikeAsync(EndSpike spike)
    {
        var spikeTop = new VectorD(spike.CenterX, spike.Height, spike.CenterZ);
        var oldCrystals = GetEntitiesInRange(spikeTop, 12).OfType<EndCrystal>().ToArray();
        foreach (var crystal in oldCrystals)
            await crystal.RemoveAsync();

        await BlastSpikeTopAsync(spike);

        await BuildSpikePillarAsync(spike);
        if (spike.Guarded)
            await BuildSpikeCageAsync(spike);

        await SetBlockAsync(new Vector(spike.CenterX, spike.Height, spike.CenterZ), BlocksRegistry.Bedrock, true);
        var crystalEntity = new EndCrystal
        {
            Level = this,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(spike.CenterX + 0.5, spike.Height + 1, spike.CenterZ + 0.5),
            Invulnerable = true
        };
        crystalEntity.SetBeam(new Vector(0, 128, 0));

        SpawnEntity(crystalEntity);
    }

    /// <summary>A resurrection clears the blocks around the spike's top and sets off an explosion there.</summary>
    private async ValueTask BlastSpikeTopAsync(EndSpike spike)
    {
        for (var x = -10; x <= 10; x++)
        {
            for (var y = -10; y <= 10; y++)
            {
                for (var z = -10; z <= 10; z++)
                {
                    var position = new Vector(spike.CenterX + x, spike.Height + y, spike.CenterZ + z);
                    await SetBlockAsync(position, BlocksRegistry.Air, true);
                }
            }
        }

        var blast = new EndCrystal
        {
            Level = this,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(spike.CenterX + 0.5, spike.Height, spike.CenterZ + 0.5)
        };
        await ExplodeAsync(blast, 5, blast, ExplosionInteraction.Block);
    }

    /// <summary>The obsidian pillar, with the air above it cleared (and the air below y 66 left alone).</summary>
    private async ValueTask BuildSpikePillarAsync(EndSpike spike)
    {
        var radius = spike.Radius;
        for (var x = -radius; x <= radius; x++)
        {
            for (var z = -radius; z <= radius; z++)
            {
                for (var y = MinY; y <= spike.Height + 10; y++)
                {
                    var block = x * x + z * z <= radius * radius + 1 && y < spike.Height
                        ? BlocksRegistry.ObsidianBlock
                        : BlocksRegistry.Air;
                    if (block.IsAir && y <= 65)
                        continue;

                    await SetBlockAsync(new Vector(spike.CenterX + x, y, spike.CenterZ + z), block, true);
                }
            }
        }
    }

    /// <summary>The iron bars caging a guarded spike's crystal.</summary>
    private async ValueTask BuildSpikeCageAsync(EndSpike spike)
    {
        for (var x = -2; x <= 2; x++)
        {
            for (var z = -2; z <= 2; z++)
            {
                for (var y = 0; y <= 3; y++)
                {
                    if (Math.Abs(x) == 2 || Math.Abs(z) == 2 || y == 3)
                    {
                        var position = new Vector(spike.CenterX + x, spike.Height + y, spike.CenterZ + z);
                        await SetBlockAsync(position, SpikeCageBars(x, y, z), true);
                    }
                }
            }
        }
    }

    private static IBlock SpikeCageBars(int x, int y, int z)
    {
        var alongX = Math.Abs(x) == 2 || y == 3;
        var alongZ = Math.Abs(z) == 2 || y == 3;
        return BlocksRegistry.IronBars.WithProperty("north", alongX && z != -2)
            .WithProperty("south", alongX && z != 2)
            .WithProperty("west", alongZ && x != -2)
            .WithProperty("east", alongZ && x != 2);
    }

    /// <summary>
    /// Reads the fight from the payload of vanilla's <c>ender_dragon_fight.dat</c>, after
    /// <see cref="ReadEndFightNbt"/> read Obsidian's own state: vanilla's file wins for what both keep, so a fight
    /// vanilla ran since Obsidian last saved the world isn't undone.
    /// </summary>
    internal void ReadVanillaEndFight(NbtCompound fight)
    {
        // Vanilla scans the end for the fight's state until it has, as Obsidian does until the fight is initialized.
        endFightInitialized = fight.TryGetBool("needs_state_scanning", out var scanning) && !scanning;
        dragonKilled = fight.TryGetBool("dragon_killed", out var killed) && killed;
        dragonPreviouslyKilled = fight.TryGetBool("previously_killed", out var previous) && previous;
        fightDragon = fight.TryGetTag<NbtArray<int>>("dragon_uuid", out var dragon) && dragon.Count == 4
            ? EntityNbt.UuidFromInts(dragon.GetArray())
            : Guid.Empty;
        if (fight.TryGetTag<NbtArray<int>>("exit_portal_location", out var portal) && portal.Count == 3)
        {
            exitPortal = new Vector(0, Math.Clamp(portal[1], MinY + 1, MinY + Height - 5), 0);
            exitPortalKnown = true;
        }

        // The gateways not created yet, which vanilla creates from the end of the list.
        if (fight.TryGetTag<NbtList>("gateways", out var gateways))
            gatewaysCreated = Math.Clamp(20 - gateways.Count, 0, 20);
    }

    /// <summary>
    /// Writes what Obsidian models of the fight to the payload of vanilla's <c>ender_dragon_fight.dat</c>, leaving the
    /// rest (like vanilla's respawn stage) as it was.
    /// </summary>
    internal void WriteVanillaEndFight(NbtCompound fight)
    {
        fight.Set(new NbtTag<byte>("needs_state_scanning", (byte)(endFightInitialized ? 0 : 1)));
        fight.Set(new NbtTag<byte>("dragon_killed", (byte)(dragonKilled ? 1 : 0)));
        fight.Set(new NbtTag<byte>("previously_killed", (byte)(dragonPreviouslyKilled ? 1 : 0)));
        fight.SetOrRemove("dragon_uuid", fightDragon == Guid.Empty
            ? null
            : new NbtArray<int>("dragon_uuid", EntityNbt.UuidToInts(fightDragon)));
        if (endFightInitialized)
            fight.Set(new NbtArray<int>("exit_portal_location", [exitPortal.X, exitPortal.Y, exitPortal.Z]));

        var gatewayOrder = ShuffledGatewayOrder();
        var gateways = new NbtList(NbtTagType.Int, "gateways");
        foreach (var gateway in gatewayOrder.Take(20 - gatewaysCreated))
            gateways.Add(new NbtTag<int>(string.Empty, gateway));
        fight.Set(gateways);
    }

    internal void WriteEndFightNbt(INbtWriter writer)
    {
        if (!HasEndFight)
            return;

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
        if (!tag.TryGetTag<NbtCompound>("ObsidianEndFight", out var fight))
            return;

        endFightInitialized = fight.TryGetBool("Initialized", out var initialized) && initialized;
        dragonKilled = fight.TryGetBool("DragonKilled", out var killed) && killed;
        dragonPreviouslyKilled = fight.TryGetBool("PreviouslyKilled", out var previous) && previous;
        if (fight.TryGetTagValue<string>("Dragon", out var dragon))
            Guid.TryParse(dragon, out fightDragon);

        if (fight.TryGetTagValue<int>("PortalY", out var y))
            exitPortal = new Vector(0, Math.Clamp(y, MinY + 1, MinY + Height - 5), 0);

        if (fight.TryGetTagValue<int>("Gateways", out var gateways))
            gatewaysCreated = Math.Clamp(gateways, 0, 20);

        if (fight.TryGetTagValue<int>("RespawnTicks", out var ticks))
            resurrectionTicks = Math.Clamp(ticks, -1, 601);

        if (fight.TryGetTagValue<string>("Crystals", out var crystals))
        {
            resurrectionCrystals = crystals.Split(',')
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToArray();
        }
    }
}
