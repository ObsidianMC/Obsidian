using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// Where a player respawns, like vanilla's <c>ServerPlayer.RespawnConfig</c>: a bed or respawn anchor's position in a
/// dimension, with the yaw the player faces there. A forced point (set by a command) needn't have a bed or anchor.
/// </summary>
public sealed record RespawnPoint(string Dimension, Vector Position, float Yaw, bool Forced);

public partial class PlayerBase
{
    // How long a player sleeps before they count towards skipping the night (vanilla's Player.SLEEP_DURATION).
    internal const int DeepSleepTicks = 100;

    // Where a sleeping player's position is in their bed's head, as vanilla's LivingEntity.setPosToBed puts it.
    private const double SleepingHeight = 0.6875;

    /// <summary>
    /// Where the player respawns; null for the world spawn.
    /// </summary>
    public RespawnPoint? RespawnPoint { get; internal set; }

    /// <summary>
    /// Whether the player has slept long enough to count towards skipping the night.
    /// </summary>
    internal bool SleptLongEnough => this.Sleeping && this.SleepTimer >= DeepSleepTicks;

    /// <summary>
    /// Sets where the player respawns, like vanilla's <c>ServerPlayer.setRespawnPosition</c>: they're told the spawn
    /// was set when it changed.
    /// </summary>
    internal async ValueTask SetRespawnPointAsync(RespawnPoint? point, bool announce)
    {
        var changed = point is null
            || this.RespawnPoint is not RespawnPoint current
            || current.Dimension != point.Dimension
            || current.Position != point.Position;

        this.RespawnPoint = point;

        if (announce && changed && point is not null)
            await this.QueuePacketAsync(new SystemChatPacket(new ChatMessage { Translate = "block.minecraft.set_spawn" }, false));
    }

    /// <summary>
    /// Puts the player to sleep in the bed whose head is at <paramref name="head"/>, like vanilla's
    /// <c>ServerPlayer.startSleepInBed</c>: the bed must be in reach and not obstructed; it sets the player's spawn
    /// where the bed rule lets it; then the rule must let them sleep now and, out of creative mode, no monster may be
    /// near.
    /// </summary>
    /// <returns>Why the player can't sleep, to show them; null once they're asleep.</returns>
    internal async ValueTask<ChatMessage?> StartSleepInBedAsync(Vector head, Vector towardsHead, BedRule rule)
    {
        if (this.Sleeping || !this.Alive || this.Level is not AbstractLevel level)
            return null;

        var canSleep = rule.AllowsSleep(level);
        var canSetSpawn = rule.AllowsSetSpawn(level);

        if (!canSleep && !canSetSpawn)
            return rule.ErrorMessage;

        if (!this.BedInRange(head, towardsHead))
            return new ChatMessage { Translate = "block.minecraft.bed.too_far_away" };

        if (this.BedObstructed(head, towardsHead))
            return new ChatMessage { Translate = "block.minecraft.bed.obstructed" };

        if (canSetSpawn)
            await this.SetRespawnPointAsync(new RespawnPoint(level.DimensionName, head, this.Yaw, Forced: false), announce: true);

        if (!canSleep)
            return rule.ErrorMessage;

        if (this.GameMode != GameMode.Creative && this.MonstersNear(head))
            return new ChatMessage { Translate = "block.minecraft.bed.not_safe" };

        await SetBedOccupiedAsync(level, head, towardsHead, true);

        this.LeaveVehicle();
        this.Motion = VectorD.Zero;
        this.Position = new VectorD(head.X + 0.5, head.Y + SleepingHeight, head.Z + 0.5);
        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
        this.BedBlockPosition = head;
        this.Pose = Pose.Sleeping;
        this.Sleeping = true;
        this.SleepTimer = 0;
        this.SyncFoodMetadata();

        return null;
    }

    /// <summary>
    /// Wakes the player, like vanilla's <c>ServerPlayer.stopSleepInBed</c>: their bed is free again and they stand up
    /// beside it.
    /// </summary>
    internal async ValueTask StopSleepingAsync()
    {
        if (!this.Sleeping)
            return;

        var head = this.BedBlockPosition;

        this.Sleeping = false;
        this.SleepTimer = 0;
        this.BedBlockPosition = null;
        this.Pose = Pose.Standing;

        if (head is Vector bed && this.Level is AbstractLevel level)
        {
            var block = await level.GetBlockAsync(bed);
            if (block is not null && TagsRegistry.Block.Beds.Entries.Contains(block.RegistryId))
            {
                var towardsHead = BedFacing(block.GetProperty("facing"));
                await SetBedOccupiedAsync(level, bed, towardsHead, false);
                this.Position = await StandUpPositionAsync(level, bed, towardsHead) ?? (VectorD)bed + new VectorD(0.5, 0.5625, 0.5);
            }
            else
            {
                this.Position = (VectorD)bed + new VectorD(0.5, 0.1, 0.5);
            }

            this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
        }

        this.PacketBroadcaster.QueuePacketToLevel(this.Level, new AnimatePacket
        {
            EntityId = this.EntityId,
            Animation = EntityAnimationType.LeaveBed
        });
        this.SyncFoodMetadata();

        await this.QueuePacketAsync(new PlayerPositionPacket
        {
            Position = this.Position,
            Yaw = this.Yaw,
            Pitch = this.Pitch,
            Delta = VectorD.Zero,
            Flags = 0,
            TeleportId = this.TeleportId = Globals.Random.Next(0, 999)
        });
    }

    /// <summary>
    /// A sleeping player's tick, like vanilla's <c>Player.tick</c>: the sleep timer runs up to its end, and the player
    /// wakes when their bed is gone or the bed rule no longer lets them sleep (the morning).
    /// </summary>
    private async ValueTask TickSleepAsync()
    {
        if (!this.Sleeping)
            return;

        if (this.SleepTimer < DeepSleepTicks)
            this.SleepTimer++;

        if (this.BedBlockPosition is not Vector head || this.Level is not AbstractLevel level)
        {
            await this.StopSleepingAsync();
            return;
        }

        var block = await level.GetBlockAsync(head);
        if (block is null
            || !TagsRegistry.Block.Beds.Entries.Contains(block.RegistryId)
            || !level.BedRuleAt(head).AllowsSleep(level))
        {
            await this.StopSleepingAsync();
        }
    }

    // Vanilla's ServerPlayer.bedInRange: the bed's head or foot within 3 blocks across and 2 up or down.
    private bool BedInRange(Vector head, Vector towardsHead)
    {
        return InReach(head) || InReach(head - towardsHead);

        bool InReach(Vector part)
        {
            var center = (VectorD)part + new VectorD(0.5, 0.5, 0.5);
            return Math.Abs(this.Position.X - center.X) <= 3
                && Math.Abs(this.Position.Y - center.Y) <= 2
                && Math.Abs(this.Position.Z - center.Z) <= 3;
        }
    }

    // Vanilla's ServerPlayer.bedBlocked: something solid over the head, and over the foot too, keeps the player out.
    private bool BedObstructed(Vector head, Vector towardsHead)
    {
        var terrain = new MobTerrain(this.Level);
        return Blocked(head) && Blocked(head - towardsHead);

        bool Blocked(Vector part)
        {
            var above = part + new Vector(0, 1, 0);
            return !terrain.IsFree(new BoundingBox((VectorD)above + new VectorD(0.1, 0, 0.1), (VectorD)above + new VectorD(0.9, 0.9, 0.9)));
        }
    }

    // Vanilla's monster check: a monster that keeps players from resting within 8 blocks across and 5 up or down.
    private bool MonstersNear(Vector head)
    {
        var center = (VectorD)head + new VectorD(0.5, 0, 0.5);
        return this.Level.GetEntitiesInRange(center, 16).Any(entity => entity is Mob { Hostile: true, Alive: true }
            && Math.Abs(entity.Position.X - center.X) <= 8
            && Math.Abs(entity.Position.Y - center.Y) <= 5
            && Math.Abs(entity.Position.Z - center.Z) <= 8);
    }

    private static async ValueTask SetBedOccupiedAsync(AbstractLevel level, Vector head, Vector towardsHead, bool occupied)
    {
        foreach (var part in new[] { head, head - towardsHead })
        {
            var block = await level.GetBlockAsync(part);
            if (block is not null && TagsRegistry.Block.Beds.Entries.Contains(block.RegistryId))
                await level.SetBlockAsync(part, block.WithProperty("occupied", occupied ? "true" : "false"), true);
        }
    }

    /// <summary>
    /// Where a player stands up from a bed, like vanilla's <c>BedBlock.findStandUpPosition</c>: a free spot with
    /// ground beside the bed, around its head then its foot, or on top of the bed if there's none.
    /// </summary>
    internal static async ValueTask<VectorD?> StandUpPositionAsync(AbstractLevel level, Vector head, Vector towardsHead)
    {
        var terrain = new MobTerrain(level);
        foreach (var part in new[] { head, head - towardsHead })
        {
            foreach (var dy in new[] { 0, 1, -1 })
            {
                for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                        continue;

                    var spot = new VectorD(part.X + dx + 0.5, part.Y + dy, part.Z + dz + 0.5);
                    if (await level.GetBlockAsync((Vector)spot.Floor()) is IBlock own && TagsRegistry.Block.Beds.Entries.Contains(own.RegistryId))
                        continue;

                    var body = new BoundingBox(spot + new VectorD(-0.3, 0, -0.3), spot + new VectorD(0.3, 1.8, 0.3));
                    var ground = new BoundingBox(spot + new VectorD(-0.3, -0.5, -0.3), spot + new VectorD(0.3, -0.01, 0.3));
                    if (terrain.IsFree(body) && terrain.IsFreeOfFluid(body) && !terrain.IsFree(ground))
                        return spot;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Where the player respawns after death, like vanilla's <c>ServerPlayer.findRespawnPositionAndUseSpawnBlock</c>:
    /// beside their bed, or their charged respawn anchor, which loses a charge; a forced point as it is. Without one,
    /// or when the bed or anchor is gone, they're told so, lose the point and respawn at the world spawn.
    /// </summary>
    internal async ValueTask<(ILevel Level, VectorD Position, float Yaw)> FindRespawnAsync()
    {
        var fallback = this.SharedWorld() ?? this.Level;
        if (this.RespawnPoint is not RespawnPoint point)
            return (fallback, fallback.LevelData.SpawnPosition, this.Yaw);

        if (this.ResolveLevel(point.Dimension) is AbstractLevel level)
        {
            var block = await level.GetBlockAsync(point.Position);
            if (block is not null && TagsRegistry.Block.Beds.Entries.Contains(block.RegistryId)
                && level.BedRuleAt(point.Position).AllowsSetSpawn(level)
                && await StandUpPositionAsync(level, point.Position, BedFacing(block.GetProperty("facing"))) is VectorD bedSpot)
            {
                return (level, bedSpot, point.Yaw);
            }

            if (block is not null && block.Is(BlockIds.RespawnAnchor)
                && int.TryParse(block.GetProperty("charges"), out var charges) && charges > 0
                && level.Gameplay(LevelEnvironment.RespawnAnchorWorks, point.Position)
                && await StandUpPositionAsync(level, point.Position, Vector.East) is VectorD anchorSpot)
            {
                if (!point.Forced)
                    await level.SetBlockAsync(point.Position, block.WithProperty("charges", charges - 1), true);

                return (level, anchorSpot, point.Yaw);
            }

            if (point.Forced)
                return (level, (VectorD)point.Position + new VectorD(0.5, 0.1, 0.5), point.Yaw);
        }

        this.RespawnPoint = null;
        await this.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.NoRespawnBlockAvailable));

        return (fallback, fallback.LevelData.SpawnPosition, this.Yaw);
    }

    // The overworld of the player's world, whose spawn is the world spawn.
    private ILevel? SharedWorld() => this.Level is IDimension child ? child.ParentWorld : this.Level;

    // The level of the player's world with the dimension name, if it has one.
    private ILevel? ResolveLevel(string dimension)
    {
        var world = this.Level is IDimension child ? child.ParentWorld as World : this.Level as World;
        return world?.DimensionName == dimension ? world : world?.dimensions.GetValueOrDefault(dimension);
    }

    private static Vector BedFacing(string? facing) => facing switch
    {
        "north" => new Vector(0, 0, -1),
        "south" => Vector.South,
        "west" => new Vector(-1, 0, 0),
        _ => Vector.East
    };

    // Vanilla's saved respawn: { dimension, pos: [I; x, y, z], yaw, pitch, forced }.
    private void WriteRespawnPoint(NbtCompound tag)
    {
        tag.Remove("respawn");
        if (this.RespawnPoint is not RespawnPoint point)
            return;

        tag.Set(new NbtCompound("respawn")
        {
            new NbtTag<string>("dimension", point.Dimension),
            new NbtArray<int>("pos", [point.Position.X, point.Position.Y, point.Position.Z]),
            new NbtTag<float>("yaw", point.Yaw),
            new NbtTag<float>("pitch", 0),
            new NbtTag<bool>("forced", point.Forced)
        });
    }

    private void ReadRespawnPoint(NbtCompound tag)
    {
        this.RespawnPoint = null;
        if (!tag.TryGetTag<NbtCompound>("respawn", out var respawn)
            || !respawn.TryGetTag<NbtArray<int>>("pos", out var pos)
            || pos.Count != 3)
        {
            return;
        }

        var point = pos.GetArray();
        this.RespawnPoint = new RespawnPoint(
            respawn.TryGetTagValue<string>("dimension", out var dimension) ? dimension : "minecraft:overworld",
            new Vector(point[0], point[1], point[2]),
            respawn.TryGetTagValue<float>("yaw", out var yaw) ? yaw : 0,
            respawn.TryGetTagValue<bool>("forced", out var forced) && forced);
    }
}
