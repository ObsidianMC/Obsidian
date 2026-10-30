using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

internal enum MobGameEvent
{
    Step, Land, Swim, ProjectileShoot, ProjectileLand, BlockPlace, BlockDestroy, BlockChange,
    EntityDamage, EntityDie, EntityInteract, NoteBlockPlay, JukeboxPlay, JukeboxStop, SculkSensorClick
}

internal readonly record struct MobVibration(
    MobGameEvent Kind,
    VectorD Position,
    IEntity? Source,
    IEntity? Owner,
    IBlock? AffectedBlock);

public abstract partial class AbstractLevel
{
    private readonly ConcurrentQueue<MobVibration> mobGameEvents = new();
    private readonly Dictionary<int, PendingMobVibration> mobVibrations = [];
    private readonly Dictionary<Vector, PendingMobVibration> blockVibrations = [];
    private readonly Dictionary<Vector, int> activeSculk = [];
    private readonly Dictionary<Guid, WardenWarning> wardenWarnings = [];
    private long gameEventTick;

    internal async ValueTask EmitMovementGameEventsAsync(Entity entity, VectorD oldPosition, MovementFlags oldFlags)
    {
        var grounded = entity.MovementFlags.HasFlag(MovementFlags.OnGround);
        if (grounded && !oldFlags.HasFlag(MovementFlags.OnGround))
        {
            EmitGameEvent(MobGameEvent.Land, entity.Position, entity);
            await TrampleTurtleEggAsync(entity, true);
        }

        var delta = entity.Position - oldPosition;
        var distance = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        if (distance <= 0.00001 || distance > 8 || entity is Warden or Allay)
            return;

        var water = GameEventBlock((Vector)entity.Position.Floor())?.RegistryId == BlockIds.Water;
        if (!grounded && !water)
            return;

        var total = entity.GameEventStepDistance + distance;
        entity.GameEventStepDistance = total < 0.6 ? total : total % 0.6;
        if (total < 0.6)
            return;

        EmitGameEvent(water ? MobGameEvent.Swim : MobGameEvent.Step, entity.Position, entity);
        if (!water)
            await TrampleTurtleEggAsync(entity);
    }

    private sealed record PendingMobVibration(MobVibration Event, long SelectedAt, long ArrivalAt, double Distance);

    private sealed record WardenWarning(int Level, long LastWarning);

    internal void EmitGameEvent(
        MobGameEvent kind,
        VectorD position,
        IEntity? source = null,
        IEntity? owner = null,
        IBlock? affectedBlock = null) =>
        mobGameEvents.Enqueue(new(kind, position, source, owner, affectedBlock));

    internal async ValueTask TickMobGameEventsAsync()
    {
        gameEventTick++;
        TickJukeboxes();
        DeliverMobVibrations();
        await DeliverBlockVibrationsAsync();
        await TickActiveSculkAsync();
        while (mobGameEvents.TryDequeue(out var vibration))
            await DispatchGameEventAsync(vibration);
    }

    /// <summary>Hands each warden and allay the vibration that has reached it this tick.</summary>
    private void DeliverMobVibrations()
    {
        foreach (var (id, pending) in mobVibrations.ToArray())
        {
            if (pending.ArrivalAt > gameEventTick)
                continue;

            mobVibrations.Remove(id);
            var listener = GetNonPlayerEntitiesInRange(pending.Event.Position, 32)
                .FirstOrDefault(entity => entity.EntityId == id);
            if (listener is Warden warden && warden.Alive && IsMobTicking(warden.Position))
                warden.ReceiveVibration(pending.Event);
            else if (listener is Allay allay && allay.Alive && IsMobTicking(allay.Position))
                allay.ReceiveVibration(pending.Event);
        }
    }

    /// <summary>Activates each sculk sensor, and sets off each shrieker, a vibration has reached this tick.</summary>
    private async ValueTask DeliverBlockVibrationsAsync()
    {
        foreach (var (point, pending) in blockVibrations.ToArray())
        {
            if (pending.ArrivalAt > gameEventTick)
                continue;

            blockVibrations.Remove(point);
            var block = GameEventBlock(point);
            if (block?.RegistryId is BlockIds.SculkSensor or BlockIds.CalibratedSculkSensor)
                await ActivateSculkSensorAsync(point, block, pending);
            else if (block?.RegistryId == BlockIds.SculkShrieker)
                await ShriekAsync(point, pending.Event.Owner ?? pending.Event.Source);
        }
    }

    private async ValueTask ActivateSculkSensorAsync(Vector point, IBlock block, PendingMobVibration pending)
    {
        if (block.GetProperty("sculk_sensor_phase") != "inactive")
            return;

        var sensorRange = block.Is(BlockIds.CalibratedSculkSensor) ? 16 : 8;
        var sensorPower = Math.Max(1, 15 - (int)(pending.Distance / sensorRange * 15));
        var activeSensor = block.WithProperty("sculk_sensor_phase", "active")
            .WithProperty("power", sensorPower);
        await SetBlockAsync(point, activeSensor, true);
        var chunk = GetLoadedChunk(point.X >> 4, point.Z >> 4);
        if (chunk?.GetBlockEntity(point.X, point.Y, point.Z) is DataBlockEntity sensor)
            sensor.Set("last_vibration_frequency", GameEventFrequency(pending.Event.Kind));

        activeSculk[point] = 30;
        var center = (VectorD)point + new VectorD(0.5, 0.5, 0.5);
        EmitGameEvent(MobGameEvent.SculkSensorClick, center, pending.Event.Source, pending.Event.Owner);
    }

    /// <summary>Counts down active sculk, and settles each block whose time is up.</summary>
    private async ValueTask TickActiveSculkAsync()
    {
        foreach (var (point, ticks) in activeSculk.ToArray())
        {
            if (!IsMobTicking((VectorD)point))
                continue;

            if (ticks > 1)
            {
                activeSculk[point] = ticks - 1;
                continue;
            }

            activeSculk.Remove(point);
            await SettleSculkAsync(point);
        }
    }

    /// <summary>
    /// A shrieker stops shrieking (and summons a warden when it was told to); a sensor goes from active to cooling
    /// down, and from cooling down to inactive.
    /// </summary>
    private async ValueTask SettleSculkAsync(Vector point)
    {
        var block = GameEventBlock(point);
        if (block?.RegistryId == BlockIds.SculkShrieker)
        {
            await SetBlockAsync(point, block.WithProperty("shrieking", false), true);
            if (shriekerSummons.Remove(point))
                TrySummonWarden(point);
        }
        else if (block?.RegistryId is BlockIds.SculkSensor or BlockIds.CalibratedSculkSensor)
        {
            var cooling = block.GetProperty("sculk_sensor_phase") == "active";
            var settled = block.WithProperty("sculk_sensor_phase", cooling ? "cooldown" : "inactive")
                .WithProperty("power", 0);
            await SetBlockAsync(point, settled, true);
            if (cooling)
                activeSculk[point] = 10;
        }
    }

    private async ValueTask DispatchGameEventAsync(MobVibration vibration)
    {
        if (vibration.Kind is MobGameEvent.JukeboxPlay or MobGameEvent.JukeboxStop)
        {
            NotifyJukeboxListeners(vibration);
            return;
        }

        if (vibration.Kind is MobGameEvent.Step or MobGameEvent.Land && vibration.Source is IPlayer)
            await ShriekUnderfootAsync(vibration);

        if (!ValidVibration(vibration))
            return;

        OfferToMobListeners(vibration);
        OfferToSculk(vibration);
    }

    private void NotifyJukeboxListeners(MobVibration vibration)
    {
        // Filtered before the query sorts its matches: most entities near a jukebox aren't allays.
        var allays = GetNonPlayerEntitiesInRange(vibration.Position, 10, static entity => entity is Allay)
            .Cast<Allay>();
        var jukebox = (Vector)vibration.Position.Floor();
        foreach (var allay in allays)
            allay.ReceiveJukeboxEvent(jukebox, vibration.Kind == MobGameEvent.JukeboxPlay);
    }

    /// <summary>A player stepping or landing on a shrieker, or on the block above one, sets it off.</summary>
    private async ValueTask ShriekUnderfootAsync(MobVibration vibration)
    {
        var feet = (Vector)vibration.Position.Floor();
        if (GameEventBlock(feet)?.RegistryId == BlockIds.SculkShrieker)
            await ShriekAsync(feet, vibration.Source);

        if (GameEventBlock(feet - new Vector(0, 1, 0))?.RegistryId == BlockIds.SculkShrieker)
            await ShriekAsync(feet - new Vector(0, 1, 0), vibration.Source);
    }

    private void OfferToMobListeners(MobVibration vibration)
    {
        // Only wardens and allays listen, so the query drops the others before sorting its matches (by entity id,
        // the order listeners are offered the vibration in).
        var listeners = GetNonPlayerEntitiesInRange(vibration.Position, 16, static entity => entity is Warden or Allay);
        foreach (var listener in listeners)
        {
            if (!IsMobTicking(listener.Position))
                continue;

            if (listener is Warden warden && !warden.AcceptsVibration(vibration))
                continue;

            if (listener is Allay allay && !allay.AcceptsVibration(vibration))
                continue;

            var receiver = listener.Position + new VectorD(0, listener.Dimension.Height * 0.85, 0);
            var distance = (receiver - vibration.Position).Magnitude;
            if (distance > 16 || WoolOccludes(vibration.Position, receiver))
                continue;

            SelectVibration(mobVibrations, listener.EntityId, vibration, distance);
        }
    }

    /// <summary>
    /// Offers the vibration to the idle sculk sensors around it, or for a sensor's click caused by a player, to the
    /// idle shriekers.
    /// </summary>
    private void OfferToSculk(MobVibration vibration)
    {
        var origin = (Vector)vibration.Position.Floor();
        foreach (var point in NearbySculk(origin, 16))
        {
            var block = GameEventBlock(point)!;
            var shrieker = block.Is(BlockIds.SculkShrieker);
            var radius = block.Is(BlockIds.CalibratedSculkSensor) ? 16 : 8;
            var click = vibration.Kind == MobGameEvent.SculkSensorClick;
            if (shrieker && !click || !shrieker && click)
                continue;

            if (shrieker && vibration.Source is not IPlayer && vibration.Owner is not IPlayer)
                continue;

            if (activeSculk.ContainsKey(point) || IsBusySculk(block, shrieker))
                continue;

            var receiver = (VectorD)point + new VectorD(0.5, 0.5, 0.5);
            var distance = (receiver - vibration.Position).Magnitude;
            if (distance > radius || WoolOccludes(vibration.Position, receiver))
                continue;

            SelectVibration(blockVibrations, point, vibration, distance);
        }
    }

    private static bool IsBusySculk(IBlock block, bool shrieker)
    {
        if (shrieker)
            return block.GetProperty("shrieking") == "true";

        return block.GetProperty("sculk_sensor_phase") != "inactive";
    }

    private void SelectVibration<TKey>(
        Dictionary<TKey, PendingMobVibration> pending,
        TKey key,
        MobVibration vibration,
        double distance)
        where TKey : notnull
    {
        if (pending.TryGetValue(key, out var previous)
            && (previous.SelectedAt != gameEventTick
                || previous.Distance < distance
                || previous.Distance == distance
                    && GameEventFrequency(previous.Event.Kind) >= GameEventFrequency(vibration.Kind)))
        {
            return;
        }

        var travelTicks = Math.Max(1, (int)Math.Floor(distance));
        pending[key] = new(vibration, gameEventTick, gameEventTick + travelTicks, distance);
    }

    private IBlock? GameEventBlock(Vector point) =>
        IsOutsideBuildHeight(point.Y) ? null : GetLoadedChunk(point.X >> 4, point.Z >> 4)?.GetBlock(point);

    private IEnumerable<Vector> NearbySculk(Vector origin, int radius, string? id = null)
    {
        for (var cx = (origin.X - radius) >> 4; cx <= (origin.X + radius) >> 4; cx++)
        {
            for (var cz = (origin.Z - radius) >> 4; cz <= (origin.Z + radius) >> 4; cz++)
            {
                // Most chunks never hold a block entity, which HasBlockEntities tells without walking the dictionary.
                if (GetLoadedChunk(cx, cz) is not Chunk chunk || !chunk.HasBlockEntities)
                    continue;

                // Read in place: each step any mob takes asks for the sculk near it.
                foreach (var (_, blockEntity) in chunk.BlockEntities)
                {
                    if (blockEntity is DataBlockEntity entity
                        && IsSculkOfId(entity, id)
                        && (entity.BlockPosition - origin).MagnitudeSquared() <= radius * radius
                        && IsMobTicking((VectorD)entity.BlockPosition))
                    {
                        yield return entity.BlockPosition;
                    }
                }
            }
        }
    }

    /// <summary>Whether the block entity has <paramref name="id"/>, or without an id, is a sensor or shrieker.</summary>
    private static bool IsSculkOfId(DataBlockEntity entity, string? id)
    {
        if (id != null)
            return entity.Id == id;

        return entity.Id is "minecraft:sculk_sensor"
            or "minecraft:calibrated_sculk_sensor"
            or "minecraft:sculk_shrieker";
    }

    private bool ValidVibration(MobVibration vibration)
    {
        if (vibration.Source is IPlayer { GameMode: GameMode.Spectator })
            return false;

        if (vibration.Source?.Sneaking == true
            && vibration.Kind is MobGameEvent.Step
                or MobGameEvent.Swim
                or MobGameEvent.ProjectileShoot
                or MobGameEvent.Land)
        {
            return false;
        }

        var point = (Vector)vibration.Position.Floor();
        if (vibration.Kind is MobGameEvent.Step or MobGameEvent.Land
            && (DampensVibrations(GameEventBlock(point))
                || DampensVibrations(GameEventBlock(point - new Vector(0, 1, 0)))))
        {
            return false;
        }

        return vibration.Kind is not (MobGameEvent.BlockPlace or MobGameEvent.BlockDestroy or MobGameEvent.BlockChange)
            || !DampensVibrations(vibration.AffectedBlock ?? GameEventBlock(point));
    }

    private static bool DampensVibrations(IBlock? block) =>
        block != null && TagsRegistry.Block.DampensVibrations.Entries.Contains(block.RegistryId);

    private static bool IsWool(IBlock? block) =>
        block != null && TagsRegistry.Block.OccludesVibrationSignals.Entries.Contains(block.RegistryId);

    private bool WoolOccludes(VectorD source, VectorD receiver)
    {
        var origin = (VectorD)(Vector)source.Floor() + new VectorD(0.5, 0.5, 0.5);
        VectorD[] offsets =
        [
            new(0.00001, 0, 0),
            new(-0.00001, 0, 0),
            new(0, 0.00001, 0),
            new(0, -0.00001, 0),
            new(0, 0, 0.00001),
            new(0, 0, -0.00001),
        ];
        return offsets.All(offset => WoolOnRay(origin + offset, receiver));
    }

    private bool WoolOnRay(VectorD start, VectorD end)
    {
        var cell = (Vector)start.Floor();
        var target = (Vector)end.Floor();
        var delta = end - start;
        var sx = Math.Sign(delta.X);
        var sy = Math.Sign(delta.Y);
        var sz = Math.Sign(delta.Z);
        var dx = sx == 0 ? double.PositiveInfinity : Math.Abs(1 / delta.X);
        var dy = sy == 0 ? double.PositiveInfinity : Math.Abs(1 / delta.Y);
        var dz = sz == 0 ? double.PositiveInfinity : Math.Abs(1 / delta.Z);
        var tx = sx == 0 ? double.PositiveInfinity : (cell.X + (sx > 0 ? 1 : 0) - start.X) / delta.X;
        var ty = sy == 0 ? double.PositiveInfinity : (cell.Y + (sy > 0 ? 1 : 0) - start.Y) / delta.Y;
        var tz = sz == 0 ? double.PositiveInfinity : (cell.Z + (sz > 0 ? 1 : 0) - start.Z) / delta.Z;
        var remaining = Math.Abs(target.X - cell.X) + Math.Abs(target.Y - cell.Y) + Math.Abs(target.Z - cell.Z) + 4;
        while (remaining-- > 0)
        {
            if (IsWool(GameEventBlock(cell)))
                return true;

            if (cell == target)
                return false;

            if (tx <= ty && tx <= tz)
            {
                cell += new Vector(sx, 0, 0);
                tx += dx;
            }
            else if (ty <= tz)
            {
                cell += new Vector(0, sy, 0);
                ty += dy;
            }
            else
            {
                cell += new Vector(0, 0, sz);
                tz += dz;
            }
        }

        return false;
    }

    private static int GameEventFrequency(MobGameEvent kind) => kind switch
    {
        MobGameEvent.Step or MobGameEvent.Swim => 1,
        MobGameEvent.ProjectileLand => 2,
        MobGameEvent.Land => 3,
        MobGameEvent.NoteBlockPlay => 10,
        MobGameEvent.BlockChange => 11,
        MobGameEvent.BlockDestroy or MobGameEvent.EntityDie => 12,
        MobGameEvent.BlockPlace => 13,
        MobGameEvent.EntityInteract => 6,
        MobGameEvent.EntityDamage => 7,
        MobGameEvent.ProjectileShoot => 3,
        MobGameEvent.SculkSensorClick => 4,
        _ => 0
    };

    private readonly HashSet<Vector> shriekerSummons = [];

    private async ValueTask ShriekAsync(Vector point, IEntity? source)
    {
        if (source is not IPlayer player || player.GameMode == GameMode.Spectator || activeSculk.ContainsKey(point))
            return;

        var block = GameEventBlock(point);
        if (block?.RegistryId != BlockIds.SculkShrieker || block.GetProperty("shrieking") == "true")
            return;

        await SetBlockAsync(point, block.WithProperty("shrieking", true), true);
        activeSculk[point] = 90;
        PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new LevelEventPacket(3007, point, 0));
        var darkened = GetPlayersInRange((VectorD)point, 40).Where(nearby => nearby.GameMode != GameMode.Spectator);
        foreach (var nearby in darkened)
            nearby.AddPotionEffect((int)PotionEffect.Darkness - 1, 260);

        if (block.GetProperty("can_summon") != "true"
            || LevelData.Difficulty == Difficulty.Peaceful
            || !LevelData.GetBooleanRule("spawn_wardens")
            || GetNonPlayerEntitiesInRange((VectorD)point, 48).Any(entity => entity is Warden))
        {
            return;
        }

        RaiseWardenWarning(point, player);
    }

    /// <summary>
    /// Raises the warning level of the shrieking player and the players near them, at most once every ten seconds; the
    /// fourth warning has the shrieker summon a warden.
    /// </summary>
    private void RaiseWardenWarning(Vector point, IPlayer player)
    {
        var players = GetPlayersInRange((VectorD)point, 16)
            .Where(nearby => nearby.GameMode != GameMode.Spectator)
            .Append(player)
            .DistinctBy(nearby => nearby.Uuid)
            .ToArray();
        var warning = players
            .Select(nearby => wardenWarnings.GetValueOrDefault(nearby.Uuid, new(0, -200)))
            .MaxBy(warning => warning.Level)!;
        var warnedRecently = players.Any(nearby => wardenWarnings.TryGetValue(nearby.Uuid, out var previous)
            && gameEventTick - previous.LastWarning < 200);
        if (warnedRecently)
            return;

        var decayed = Math.Max(0, warning.Level - (int)((gameEventTick - warning.LastWarning) / 12000));
        var next = new WardenWarning(Math.Min(4, decayed + 1), gameEventTick);
        foreach (var nearby in players)
            wardenWarnings[nearby.Uuid] = next;

        if (next.Level == 4)
            shriekerSummons.Add(point);
    }

    private void TrySummonWarden(Vector point)
    {
        if (!LevelData.GetBooleanRule("spawn_wardens")
            || LevelData.Difficulty == Difficulty.Peaceful
            || GetNonPlayerEntitiesInRange((VectorD)point, 48).Any(entity => entity is Warden))
        {
            return;
        }

        var terrain = new MobTerrain(this);
        var wardenSize = new EntityDimension { Width = 0.9f, Height = 2.9f };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var offset = new Vector(Random.Shared.Next(-5, 6), Random.Shared.Next(-6, 7), Random.Shared.Next(-5, 6));
            var spawn = point + offset;
            var position = (VectorD)spawn + new VectorD(0.5, 0, 0.5);
            if (terrain.GetBlock(spawn - new Vector(0, 1, 0)) is not { IsAir: false, IsLiquid: false }
                || !terrain.IsFree(wardenSize.CreateBBFromPosition(position)))
            {
                continue;
            }

            var warden = new Warden { Level = this, EntityId = Server.GetNextEntityId(), Position = position };
            warden.BeginEmerging();
            SpawnEntity(warden);
            return;
        }
    }
}
