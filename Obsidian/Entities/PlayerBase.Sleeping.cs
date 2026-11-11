using Obsidian.API.Blocks;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.WorldData.Features;

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

    // How long a player woken without being forced keeps counting, the client's fade out (vanilla's Player.tick).
    private const int WakeTimerEnd = 110;

    // Whether the bed the player sleeps in set their spawn, which only then counts as having slept in a bed for
    // advancements (vanilla's SLEPT_IN_BED trigger).
    internal bool SleepSetSpawn { get; private set; }

    /// <summary>
    /// Puts the player to sleep in the bed or straw bed whose head is at <paramref name="head"/>, like vanilla's
    /// <c>ServerPlayer.startSleepInBed</c>: the bed must be in reach and not obstructed; it sets the player's spawn
    /// where the bed's rule lets it; then the rule must let them sleep now and, out of creative mode, no monster may be
    /// near.
    /// The bed's statistic is awarded, and the players are told how many sleep.
    /// </summary>
    /// <returns>Why the player can't sleep, to show them; null once they're asleep, or when nothing is said.</returns>
    internal async ValueTask<ChatMessage?> StartSleepInBedAsync(Vector head, IBlock bed, BedRule rule)
    {
        if (this.Sleeping || !this.Alive || this.Level is not AbstractLevel level)
            return null;

        var towardsHead = BedBlocks.Facing(bed);
        var canSleep = rule.AllowsSleep(level);
        var canSetSpawn = rule.AllowsSetSpawn(level);

        if (!this.BedInRange(head, towardsHead))
            return new ChatMessage { Translate = "block.minecraft.bed.too_far_away" };

        if (this.BedObstructed(head, towardsHead))
            return new ChatMessage { Translate = "block.minecraft.bed.obstructed" };

        if (canSetSpawn)
        {
            var point = new RespawnPoint(level.DimensionName, head, this.Yaw, Forced: false);
            await this.SetRespawnPointAsync(point, announce: true);
        }

        if (!canSleep)
            return rule.ErrorMessage;

        if (this.GameMode != GameMode.Creative && this.MonstersNear(head))
            return new ChatMessage { Translate = "block.minecraft.bed.not_safe" };

        // Vanilla's LivingEntity.startSleeping: nobody lies on a straw bed's head without its foot.
        if (BedBlocks.SleepHeight(level.BlockUpdates.GetBlock, head, bed) is not double height)
            return null;

        this.LeaveVehicle();
        this.Position = new VectorD(head.X + 0.5, head.Y + height, head.Z + 0.5);
        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);

        // Only the head is written; its foot follows through its shape update.
        await level.SetBlockAsync(head, bed.WithProperty("occupied", "true"), BlockUpdateFlags.All);

        this.Motion = VectorD.Zero;
        this.BedBlockPosition = head;
        this.Pose = Pose.Sleeping;
        this.Sleeping = true;
        this.SleepTimer = 0;
        this.SleepSetSpawn = canSetSpawn;
        this.SyncFoodMetadata();

        var statistic = bed.Is(BlockIds.StrawBed) ? "minecraft:sleep_in_straw_bed" : "minecraft:sleep_in_bed";
        this.Statistics.Increment(statistic);

        await level.UpdateSleepingPlayersAsync();
        return null;
    }

    /// <summary>
    /// Wakes the player, like vanilla's <c>ServerPlayer.stopSleepInBed</c>: their bed is free again, a bed whose rule
    /// says so (a straw bed's) breaks, and they stand up beside it facing it, where vanilla's
    /// <c>findStandUpPosition</c> finds room, or on top of it.
    /// </summary>
    /// <param name="forceful">
    /// Whether they were woken by force (their bed gone, death), which resets the sleep timer; otherwise it runs on for
    /// the client's fade, as after leaving the bed or the night passing.
    /// </param>
    /// <param name="announce">Whether the players are told how many sleep now; not when everyone wakes at once.</param>
    internal async ValueTask StopSleepingAsync(bool forceful = true, bool announce = true)
    {
        if (!this.Sleeping)
            return;

        var head = this.BedBlockPosition;

        this.Sleeping = false;
        this.BedBlockPosition = null;
        this.Pose = Pose.Standing;
        this.SleepSetSpawn = false;

        var position = this.Position;
        if (head is Vector bed && this.Level is AbstractLevel level)
        {
            var block = await level.GetBlockAsync(bed);
            if (BedBlocks.IsBed(block))
            {
                var facing = BedBlocks.Facing(block!);
                await level.SetBlockAsync(bed, block!.WithProperty("occupied", "false"), BlockUpdateFlags.All);

                // Vanilla's onStopSleeping: a bed destroyed on leaving, as straw beds are.
                if (level.BedRuleAt(bed, block).DestroyOnLeave && block.Is(BlockIds.StrawBed))
                    await level.BreakStrawBedAsync(bed);

                var standUp = BedBlocks.StandUpPosition(level, bed, facing, this.Yaw);
                position = standUp ?? (VectorD)bed + new VectorD(0.5, 1.1, 0.5);
                this.Yaw = BedBlocks.YawTowards(bed, position);
                this.Pitch = 0;
            }
        }

        this.SleepTimer = (short)(forceful ? 0 : DeepSleepTicks);
        this.PacketBroadcaster.QueuePacketToLevel(this.Level, new AnimatePacket
        {
            EntityId = this.EntityId,
            Animation = EntityAnimationType.WakeUp
        });
        this.SyncFoodMetadata();
        await this.SendPositionAsync(VectorD.Zero, position: position);

        if (announce && this.Level is AbstractLevel announcing)
            await announcing.UpdateSleepingPlayersAsync();
    }

    /// <summary>
    /// A player's sleep tick, like vanilla's <c>Player.tick</c>: the sleep timer runs up to its end; the player wakes
    /// when their bed's rule no longer lets them sleep (the morning), or by force when their bed is gone. Awake, the
    /// timer runs on to the end of the client's fade.
    /// </summary>
    private async ValueTask TickSleepAsync()
    {
        if (!this.Sleeping)
        {
            if (this.SleepTimer > 0 && ++this.SleepTimer >= WakeTimerEnd)
                this.SleepTimer = 0;

            return;
        }

        if (this.SleepTimer < DeepSleepTicks)
            this.SleepTimer++;

        if (this.BedBlockPosition is not Vector head || this.Level is not AbstractLevel level)
        {
            await this.StopSleepingAsync();
            return;
        }

        var block = await level.GetBlockAsync(head);
        if (!BedBlocks.IsBed(block) || BedBlocks.SleepHeight(level.BlockUpdates.GetBlock, head, block!) is null)
        {
            await this.StopSleepingAsync();
            return;
        }

        if (!level.BedRuleAt(head, block!).AllowsSleep(level))
            await this.StopSleepingAsync(forceful: false);
    }

    // Vanilla's ServerPlayer.bedInRange: the bed's head or foot within 3 blocks across and 2 up or down of its bottom
    // centre.
    private bool BedInRange(Vector head, BlockFace towardsHead)
    {
        return InReach(head) || InReach(head.Offset(towardsHead.Opposite()));

        bool InReach(Vector part)
        {
            var center = (VectorD)part + new VectorD(0.5, 0, 0.5);
            return Math.Abs(this.Position.X - center.X) <= 3
                && Math.Abs(this.Position.Y - center.Y) <= 2
                && Math.Abs(this.Position.Z - center.Z) <= 3;
        }
    }

    // Vanilla's ServerPlayer.bedBlocked: something suffocating over the head, or over the foot, keeps the player out.
    private bool BedObstructed(Vector head, BlockFace towardsHead)
    {
        var terrain = new MobTerrain(this.Level);
        return Blocked(head) || Blocked(head.Offset(towardsHead.Opposite()));

        bool Blocked(Vector part)
        {
            var above = part + new Vector(0, 1, 0);
            var cell = new BoundingBox((VectorD)above, (VectorD)above + new VectorD(1, 1, 1));
            return terrain.ContainsSuffocatingBlock(cell);
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

    /// <summary>
    /// Where a player respawns beside a respawn anchor at <paramref name="head"/>: a free spot with ground around it,
    /// level with it, then above, then below. Vanilla's <c>RespawnAnchorBlock.findStandUpPosition</c> searches its own
    /// offsets, which this doesn't follow yet.
    /// </summary>
    internal static async ValueTask<VectorD?> StandUpPositionAsync(AbstractLevel level, Vector head, Vector towardsHead)
    {
        var terrain = new MobTerrain(level);
        foreach (var part in new[] { head, head - towardsHead })
        {
            foreach (var dy in new[] { 0, 1, -1 })
            {
                for (var dx = -1; dx <= 1; dx++)
                {
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
            if (BedBlocks.IsBed(block) && level.BedRuleAt(point.Position, block!).AllowsSetSpawn(level))
            {
                var facing = BedBlocks.Facing(block!);
                if (BedBlocks.StandUpPosition(level, point.Position, facing, point.Yaw) is VectorD bedSpot)
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

            // Vanilla's isPossibleToRespawnInThis: a forced point needs room where its block and the one above are.
            var above = await level.GetBlockAsync(point.Position + Vector.Up);
            var roomy = RespawnsInside(level, point.Position, block)
                && RespawnsInside(level, point.Position + Vector.Up, above);
            if (point.Forced && roomy)
                return (level, (VectorD)point.Position + new VectorD(0.5, 0.1, 0.5), point.Yaw);
        }

        this.RespawnPoint = null;
        await this.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.NoRespawnBlockAvailable));

        return (fallback, fallback.LevelData.SpawnPosition, this.Yaw);
    }

    // Vanilla's Block.isPossibleToRespawnInThis: nothing solid or liquid. A block with a collision shape counts as
    // solid here, which vanilla's legacy solid flag mostly agrees with.
    private static bool RespawnsInside(ILevel level, Vector position, IBlock? block) =>
        block is not null
        && !block.IsLiquid
        && BlockCollisionShapes.Get(level, position, block).Count == 0;

    // The overworld of the player's world, whose spawn is the world spawn.
    private ILevel? SharedWorld() => this.Level is IDimension child ? child.ParentWorld : this.Level;

    // The level of the player's world with the dimension name, if it has one.
    private ILevel? ResolveLevel(string dimension)
    {
        var world = this.Level is IDimension child ? child.ParentWorld as World : this.Level as World;
        return world?.DimensionName == dimension ? world : world?.dimensions.GetValueOrDefault(dimension);
    }

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
            respawn.TryGetNumber<float>("yaw", out var yaw) ? yaw : 0,
            respawn.TryGetBool("forced", out var forced) && forced);
    }
}
