using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

internal enum MobGameEvent
{
    Step, Land, Swim, ProjectileShoot, ProjectileLand, BlockPlace, BlockDestroy, BlockChange,
    EntityDamage, EntityDie, EntityInteract, NoteBlockPlay, JukeboxPlay, JukeboxStop, SculkSensorClick
}

internal readonly record struct MobVibration(MobGameEvent Kind, VectorD Position, IEntity? Source, IEntity? Owner, IBlock? AffectedBlock);

public abstract partial class AbstractLevel
{
    private readonly ConcurrentQueue<MobVibration> mobGameEvents = new();
    private readonly Dictionary<int, PendingMobVibration> mobVibrations = [];
    private readonly Dictionary<Vector, PendingMobVibration> blockVibrations = [];
    private readonly Dictionary<Vector, int> activeSculk = [];
    private readonly Dictionary<Guid, WardenWarning> wardenWarnings = [];
    private long gameEventTick;
    private readonly ConcurrentDictionary<Guid, double> stepDistances = new();

    internal async ValueTask EmitMovementGameEventsAsync(IEntity entity, VectorD oldPosition, MovementFlags oldFlags)
    {
        var grounded = entity.MovementFlags.HasFlag(MovementFlags.OnGround);
        if (grounded && !oldFlags.HasFlag(MovementFlags.OnGround))
        {
            EmitGameEvent(MobGameEvent.Land, entity.Position, entity);
            await TrampleTurtleEggAsync(entity, true);
        }
        var delta = entity.Position - oldPosition;
        var distance = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        if (distance <= 0.00001 || distance > 8 || entity is Warden or Allay) return;
        var water = GameEventBlock((Vector)entity.Position.Floor())?.Material == Material.Water;
        if (!grounded && !water) return;
        var total = stepDistances.AddOrUpdate(entity.Uuid, distance, (_, accumulated) => accumulated + distance);
        if (total < 0.6) return;
        stepDistances[entity.Uuid] = total % 0.6;
        EmitGameEvent(water ? MobGameEvent.Swim : MobGameEvent.Step, entity.Position, entity);
        if (!water) await TrampleTurtleEggAsync(entity);
    }

    private sealed record PendingMobVibration(MobVibration Event, long SelectedAt, long ArrivalAt, double Distance);
    private sealed record WardenWarning(int Level, long LastWarning);

    internal void EmitGameEvent(MobGameEvent kind, VectorD position, IEntity? source = null, IEntity? owner = null, IBlock? affectedBlock = null) =>
        mobGameEvents.Enqueue(new(kind, position, source, owner, affectedBlock));

    internal async ValueTask TickMobGameEventsAsync()
    {
        gameEventTick++;
        TickJukeboxes();
        foreach (var (id, pending) in mobVibrations.ToArray())
        {
            if (pending.ArrivalAt > gameEventTick) continue;
            mobVibrations.Remove(id);
            var listener = GetNonPlayerEntitiesInRange(pending.Event.Position, 32).FirstOrDefault(entity => entity.EntityId == id);
            if (listener is Warden warden && warden.Alive && IsMobTicking(warden.Position)) warden.ReceiveVibration(pending.Event);
            else if (listener is Allay allay && allay.Alive && IsMobTicking(allay.Position)) allay.ReceiveVibration(pending.Event);
        }
        foreach (var (point, pending) in blockVibrations.ToArray())
        {
            if (pending.ArrivalAt > gameEventTick) continue;
            blockVibrations.Remove(point);
            var block = GameEventBlock(point);
            if (block?.Material is Material.SculkSensor or Material.CalibratedSculkSensor)
            {
                if (block.GetProperty("sculk_sensor_phase") != "inactive") continue;
                await SetBlockAsync(point, block.WithProperty("sculk_sensor_phase", "active").WithProperty("power", Math.Max(1, 15 - (int)(pending.Distance / (block.Material == Material.CalibratedSculkSensor ? 16 : 8) * 15))), true);
                if (GetLoadedChunk(point.X >> 4, point.Z >> 4)?.GetBlockEntity(point.X, point.Y, point.Z) is DataBlockEntity sensor)
                    sensor.Set("last_vibration_frequency", GameEventFrequency(pending.Event.Kind));
                activeSculk[point] = 30;
                EmitGameEvent(MobGameEvent.SculkSensorClick, (VectorD)point + new VectorD(0.5, 0.5, 0.5), pending.Event.Source, pending.Event.Owner);
            }
            else if (block?.Material == Material.SculkShrieker)
                await ShriekAsync(point, pending.Event.Owner ?? pending.Event.Source);
        }
        foreach (var (point, ticks) in activeSculk.ToArray())
        {
            if (!IsMobTicking((VectorD)point)) continue;
            if (ticks > 1) { activeSculk[point] = ticks - 1; continue; }
            activeSculk.Remove(point);
            var block = GameEventBlock(point);
            if (block?.Material == Material.SculkShrieker)
            {
                await SetBlockAsync(point, block.WithProperty("shrieking", false), true);
                if (shriekerSummons.Remove(point)) TrySummonWarden(point);
            }
            else if (block?.Material is Material.SculkSensor or Material.CalibratedSculkSensor)
            {
                var cooling = block.GetProperty("sculk_sensor_phase") == "active";
                await SetBlockAsync(point, block.WithProperty("sculk_sensor_phase", cooling ? "cooldown" : "inactive").WithProperty("power", 0), true);
                if (cooling) activeSculk[point] = 10;
            }
        }
        while (mobGameEvents.TryDequeue(out var vibration))
        {
            if (vibration.Kind is MobGameEvent.JukeboxPlay or MobGameEvent.JukeboxStop)
            {
                foreach (var allay in GetNonPlayerEntitiesInRange(vibration.Position, 10).OfType<Allay>())
                    allay.ReceiveJukeboxEvent((Vector)vibration.Position.Floor(), vibration.Kind == MobGameEvent.JukeboxPlay);
                continue;
            }
            if (vibration.Kind is MobGameEvent.Step or MobGameEvent.Land && vibration.Source is IPlayer)
            {
                var feet = (Vector)vibration.Position.Floor();
                if (GameEventBlock(feet)?.Material == Material.SculkShrieker) await ShriekAsync(feet, vibration.Source);
                if (GameEventBlock(feet - new Vector(0, 1, 0))?.Material == Material.SculkShrieker) await ShriekAsync(feet - new Vector(0, 1, 0), vibration.Source);
            }
            if (!ValidVibration(vibration)) continue;
            foreach (var listener in GetNonPlayerEntitiesInRange(vibration.Position, 16))
            {
                if (!IsMobTicking(listener.Position)) continue;
                if (listener is Warden warden && !warden.AcceptsVibration(vibration)) continue;
                if (listener is Allay allay && !allay.AcceptsVibration(vibration)) continue;
                if (listener is not Warden and not Allay) continue;
                var receiver = listener.Position + new VectorD(0, listener.Dimension.Height * 0.85, 0);
                var distance = (receiver - vibration.Position).Magnitude;
                if (distance > 16 || WoolOccludes(vibration.Position, receiver)) continue;
                SelectVibration(mobVibrations, listener.EntityId, vibration, distance);
            }
            var origin = (Vector)vibration.Position.Floor();
            foreach (var point in NearbySculk(origin, 16))
            {
                var block = GameEventBlock(point)!;
                var shrieker = block.Material == Material.SculkShrieker;
                var radius = block.Material == Material.CalibratedSculkSensor ? 16 : 8;
                if (shrieker && vibration.Kind != MobGameEvent.SculkSensorClick || !shrieker && vibration.Kind == MobGameEvent.SculkSensorClick) continue;
                if (shrieker && vibration.Source is not IPlayer && vibration.Owner is not IPlayer) continue;
                if (activeSculk.ContainsKey(point) || (shrieker ? block.GetProperty("shrieking") == "true" : block.GetProperty("sculk_sensor_phase") != "inactive")) continue;
                var receiver = (VectorD)point + new VectorD(0.5, 0.5, 0.5);
                var distance = (receiver - vibration.Position).Magnitude;
                if (distance > radius || WoolOccludes(vibration.Position, receiver)) continue;
                SelectVibration(blockVibrations, point, vibration, distance);
            }
        }
    }

    private void SelectVibration<TKey>(Dictionary<TKey, PendingMobVibration> pending, TKey key, MobVibration vibration, double distance) where TKey : notnull
    {
        if (pending.TryGetValue(key, out var previous) && (previous.SelectedAt != gameEventTick ||
            previous.Distance < distance || previous.Distance == distance && GameEventFrequency(previous.Event.Kind) >= GameEventFrequency(vibration.Kind))) return;
        pending[key] = new(vibration, gameEventTick, gameEventTick + Math.Max(1, (int)Math.Floor(distance)), distance);
    }

    private IBlock? GameEventBlock(Vector point) => IsOutsideBuildHeight(point.Y) ? null : GetLoadedChunk(point.X >> 4, point.Z >> 4)?.GetBlock(point);

    private IEnumerable<Vector> NearbySculk(Vector origin, int radius, string? id = null)
    {
        for (var cx = (origin.X - radius) >> 4; cx <= (origin.X + radius) >> 4; cx++)
        for (var cz = (origin.Z - radius) >> 4; cz <= (origin.Z + radius) >> 4; cz++)
        {
            if (GetLoadedChunk(cx, cz) is not { } chunk) continue;
            foreach (var entity in chunk.GetBlockEntities().OfType<DataBlockEntity>())
                if ((id == null ? entity.Id is "minecraft:sculk_sensor" or "minecraft:calibrated_sculk_sensor" or "minecraft:sculk_shrieker" : entity.Id == id) &&
                    (entity.BlockPosition - origin).MagnitudeSquared() <= radius * radius && IsMobTicking((VectorD)entity.BlockPosition))
                    yield return entity.BlockPosition;
        }
    }

    private bool ValidVibration(MobVibration vibration)
    {
        if (vibration.Source is IPlayer { GameMode: GameMode.Spectator }) return false;
        if (vibration.Source?.Sneaking == true && vibration.Kind is MobGameEvent.Step or MobGameEvent.Swim or MobGameEvent.ProjectileShoot or MobGameEvent.Land) return false;
        var point = (Vector)vibration.Position.Floor();
        if (vibration.Kind is MobGameEvent.Step or MobGameEvent.Land &&
            (DampensVibrations(GameEventBlock(point)) || DampensVibrations(GameEventBlock(point - new Vector(0, 1, 0))))) return false;
        return vibration.Kind is not (MobGameEvent.BlockPlace or MobGameEvent.BlockDestroy or MobGameEvent.BlockChange) || !DampensVibrations(vibration.AffectedBlock ?? GameEventBlock(point));
    }

    private static bool DampensVibrations(IBlock? block) => block != null && TagsRegistry.Block.DampensVibrations.Entries.Contains(block.RegistryId);
    private static bool IsWool(IBlock? block) => block != null && TagsRegistry.Block.OccludesVibrationSignals.Entries.Contains(block.RegistryId);

    private bool WoolOccludes(VectorD source, VectorD receiver)
    {
        var origin = (VectorD)(Vector)source.Floor() + new VectorD(0.5, 0.5, 0.5);
        VectorD[] offsets = [new(0.00001, 0, 0), new(-0.00001, 0, 0), new(0, 0.00001, 0), new(0, -0.00001, 0), new(0, 0, 0.00001), new(0, 0, -0.00001)];
        return offsets.All(offset => WoolOnRay(origin + offset, receiver));
    }

    private bool WoolOnRay(VectorD start, VectorD end)
    {
        var cell = (Vector)start.Floor();
        var target = (Vector)end.Floor();
        var delta = end - start;
        var sx = Math.Sign(delta.X); var sy = Math.Sign(delta.Y); var sz = Math.Sign(delta.Z);
        var dx = sx == 0 ? double.PositiveInfinity : Math.Abs(1 / delta.X);
        var dy = sy == 0 ? double.PositiveInfinity : Math.Abs(1 / delta.Y);
        var dz = sz == 0 ? double.PositiveInfinity : Math.Abs(1 / delta.Z);
        var tx = sx == 0 ? double.PositiveInfinity : (cell.X + (sx > 0 ? 1 : 0) - start.X) / delta.X;
        var ty = sy == 0 ? double.PositiveInfinity : (cell.Y + (sy > 0 ? 1 : 0) - start.Y) / delta.Y;
        var tz = sz == 0 ? double.PositiveInfinity : (cell.Z + (sz > 0 ? 1 : 0) - start.Z) / delta.Z;
        var remaining = Math.Abs(target.X - cell.X) + Math.Abs(target.Y - cell.Y) + Math.Abs(target.Z - cell.Z) + 4;
        while (remaining-- > 0)
        {
            if (IsWool(GameEventBlock(cell))) return true;
            if (cell == target) return false;
            if (tx <= ty && tx <= tz) { cell += new Vector(sx, 0, 0); tx += dx; }
            else if (ty <= tz) { cell += new Vector(0, sy, 0); ty += dy; }
            else { cell += new Vector(0, 0, sz); tz += dz; }
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
        if (source is not IPlayer player || player.GameMode == GameMode.Spectator || activeSculk.ContainsKey(point)) return;
        var block = GameEventBlock(point);
        if (block?.Material != Material.SculkShrieker || block.GetProperty("shrieking") == "true") return;
        await SetBlockAsync(point, block.WithProperty("shrieking", true), true);
        activeSculk[point] = 90;
        PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new LevelEventPacket(3007, point, 0));
        foreach (var nearby in GetPlayersInRange((VectorD)point, 40).Where(nearby => nearby.GameMode != GameMode.Spectator))
            nearby.AddPotionEffect((int)PotionEffect.Darkness - 1, 260);
        if (block.GetProperty("can_summon") != "true" || LevelData.Difficulty == Difficulty.Peaceful || !LevelData.GetBooleanRule("spawn_wardens") ||
            GetNonPlayerEntitiesInRange((VectorD)point, 48).Any(entity => entity is Warden)) return;
        var players = GetPlayersInRange((VectorD)point, 16).Where(nearby => nearby.GameMode != GameMode.Spectator).Append(player).DistinctBy(nearby => nearby.Uuid).ToArray();
        var warning = players.Select(nearby => wardenWarnings.GetValueOrDefault(nearby.Uuid, new(0, -200))).MaxBy(warning => warning.Level)!;
        if (players.Any(nearby => wardenWarnings.TryGetValue(nearby.Uuid, out var previous) && gameEventTick - previous.LastWarning < 200)) return;
        var decayed = Math.Max(0, warning.Level - (int)((gameEventTick - warning.LastWarning) / 12000));
        var next = new WardenWarning(Math.Min(4, decayed + 1), gameEventTick);
        foreach (var nearby in players) wardenWarnings[nearby.Uuid] = next;
        if (next.Level == 4) shriekerSummons.Add(point);
    }

    private void TrySummonWarden(Vector point)
    {
        if (!LevelData.GetBooleanRule("spawn_wardens") || LevelData.Difficulty == Difficulty.Peaceful || GetNonPlayerEntitiesInRange((VectorD)point, 48).Any(entity => entity is Warden)) return;
        var terrain = new MobTerrain(this);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var spawn = point + new Vector(Random.Shared.Next(-5, 6), Random.Shared.Next(-6, 7), Random.Shared.Next(-5, 6));
            var position = (VectorD)spawn + new VectorD(0.5, 0, 0.5);
            if (terrain.GetBlock(spawn - new Vector(0, 1, 0)) is not { IsAir: false, IsLiquid: false } ||
                !terrain.IsFree(new EntityDimension { Width = 0.9f, Height = 2.9f }.CreateBBFromPosition(position))) continue;
            var warden = new Warden { Level = this, EntityId = Server.GetNextEntityId(), Position = position };
            warden.BeginEmerging();
            SpawnEntity(warden);
            return;
        }
    }
}
