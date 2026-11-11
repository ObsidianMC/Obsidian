using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // The power of the explosion of a bed or respawn anchor where it doesn't work, which sets fires (vanilla's
    // BedBlock and RespawnAnchorBlock).
    private const float BadRespawnPointPower = 5;

    internal const int MaxAnchorCharges = 4;

    // The level event of a straw bed breaking, which its clients show with straw particles (vanilla's
    // StrawBedBlock.spawnDestroyParticles).
    internal const int StrawBedBreakEvent = 2014;

    // How many players were asleep at the last tick, to tell players when it changes (vanilla's SleepStatus).
    private int previousSleepers;

    /// <summary>
    /// Skips the night like vanilla's <c>ServerLevel.tick</c>: once enough players (by <c>players_sleeping_percentage</c>,
    /// of those not spectating) are asleep, and have slept long enough, the time moves on to the next morning where
    /// <c>advance_time</c> lets it, the rain stops where <c>advance_weather</c> lets it, and everyone wakes. Players are
    /// told how many sleep whenever that changes.
    /// </summary>
    private async ValueTask TickSleepingAsync()
    {
        var players = this.Players.Values.OfType<PlayerBase>()
            .Where(player => player.GameMode != GameMode.Spectator)
            .ToArray();
        if (players.Length == 0)
        {
            previousSleepers = 0;
            return;
        }

        await this.AnnounceSleepersAsync(players);

        var percentage = LevelData.GetIntegerRule("players_sleeping_percentage", 100);
        var sleeping = players.Count(player => player.Sleeping);
        var needed = Math.Max(1, (int)Math.Ceiling(players.Length * percentage / 100.0));

        if (sleeping < needed || players.Count(player => player.SleptLongEnough) < needed)
            return;

        // ServerLevel uses the dimension's optional default clock, and only that clock's wake-up marker.
        if (LevelData.GetBooleanRule("advance_time")
            && WorldTimelines.DefaultClock(this) is string clockId
            && WorldTimelines.Markers.TryGetValue("minecraft:wake_up_from_sleep", out var marker)
            && marker.Clock == clockId
            && LevelData.WorldClocks.States.TryGetValue(clockId, out var clock)
            // Levels sharing the clock can skip the night at once, so only the first to reach it moves it.
            && clock.TrySetTime(time => marker.OccursAt(time) ? null : marker.Next(time)) is not null)
        {
            this.BroadcastTime();
        }

        // Vanilla's wakeUpAllPlayers: they wake without the sleepers being announced again.
        foreach (var player in players.Where(player => player.Sleeping))
            await player.StopSleepingAsync(forceful: false, announce: false);

        if (LevelData.GetBooleanRule("advance_weather") && (LevelData.Raining || LevelData.Thundering))
        {
            LevelData.RainTime = 0;
            LevelData.Raining = false;
            LevelData.ThunderTime = 0;
            LevelData.Thundering = false;

            PacketBroadcaster.QueuePacketToLevel(this, new GameEventPacket(ChangeGameStateReason.EndRaining));
            PacketBroadcaster.QueuePacketToLevel(this, new GameEventPacket(ChangeGameStateReason.RainLevelChange, 0));
            PacketBroadcaster.QueuePacketToLevel(this, new GameEventPacket(ChangeGameStateReason.ThunderLevelChange, 0));
        }

        previousSleepers = 0;
    }

    /// <summary>
    /// Vanilla's <c>updateSleepingPlayerList</c>, when a player falls asleep or wakes: the players are told how many
    /// sleep, once that changed.
    /// </summary>
    internal ValueTask UpdateSleepingPlayersAsync()
    {
        var players = this.Players.Values.OfType<PlayerBase>()
            .Where(player => player.GameMode != GameMode.Spectator)
            .ToArray();

        return this.AnnounceSleepersAsync(players);
    }

    // Vanilla's SleepStatus.update and announceSleepStatus: when the number of sleepers changed, and anyone sleeps or
    // slept, every player is told that the night is being skipped or how many sleep of how many are needed.
    private async ValueTask AnnounceSleepersAsync(PlayerBase[] players)
    {
        var percentage = LevelData.GetIntegerRule("players_sleeping_percentage", 100);
        var sleeping = players.Count(player => player.Sleeping);
        var needed = Math.Max(1, (int)Math.Ceiling(players.Length * percentage / 100.0));

        var changed = sleeping != previousSleepers && (sleeping > 0 || previousSleepers > 0);
        previousSleepers = sleeping;
        if (!changed)
            return;

        var status = sleeping >= needed
            ? new ChatMessage { Translate = "sleep.skipping_night" }
            : new ChatMessage { Translate = "sleep.players_sleeping" }
                .AddChatComponent(sleeping.ToString())
                .AddChatComponent(needed.ToString());

        foreach (var player in players)
            await player.QueuePacketAsync(new SystemChatPacket(status, overlay: true));
    }

    // The player inventory's offhand slot.
    private const int OffhandSlot = 45;

    /// <summary>
    /// A player using a bed or straw bed, like vanilla's <c>AbstractBedBlock.useWithoutItem</c>: a half without its
    /// other half does nothing. Where the bed's rule destroys it on use (the nether's and the end's), it's removed, a
    /// bed exploding and a straw bed breaking, with the rule's message if it has one; otherwise a player is told the
    /// bed is occupied, or tries to sleep in it (see <see cref="PlayerBase.StartSleepInBedAsync"/>).
    /// </summary>
    /// <returns>Pass when <paramref name="block"/> isn't a bed; how the use went otherwise.</returns>
    internal async ValueTask<UseResult> UseBedAsync(IPlayer player, Vector position, IBlock block)
    {
        if (!BedBlocks.IsBed(block))
            return UseResult.Pass;

        // The use is about the head; the other half must be there, the same bed's other part.
        var facing = BedBlocks.Facing(block);
        var part = block.GetProperty("part");
        var isHead = part == "head";
        var otherPosition = position.Offset(isHead ? facing.Opposite() : facing);
        var other = GameEventBlock(otherPosition);
        if (other is null || other.RegistryId != block.RegistryId || other.GetProperty("part") == part)
            return UseResult.Consume;

        var head = isHead ? position : otherPosition;
        var headBlock = isHead ? block : other;
        var rule = this.BedRuleAt(head, headBlock);

        if (rule.DestroyOnUse)
        {
            if (rule.ErrorMessage is ChatMessage reason)
                await player.QueuePacketAsync(new SystemChatPacket(reason, overlay: true));

            if (headBlock.Is(BlockIds.StrawBed))
                await this.BreakStrawBedAsync(head);
            else
                await this.ExplodeBedAsync(head, headBlock);

            return UseResult.SuccessServer;
        }

        if (headBlock.GetProperty("occupied") == "true")
        {
            var occupied = new ChatMessage { Translate = "block.minecraft.bed.occupied" };
            await player.QueuePacketAsync(new SystemChatPacket(occupied, overlay: true));
            return UseResult.SuccessServer;
        }

        if (player is PlayerBase sleeper
            && await sleeper.StartSleepInBedAsync(head, headBlock, rule) is ChatMessage problem)
        {
            await player.QueuePacketAsync(new SystemChatPacket(problem, overlay: true));
        }

        return UseResult.SuccessServer;
    }

    /// <summary>
    /// Vanilla's <c>StrawBedBlock.destroyBed</c>: the straw bed's break sound, and its head set to air with every
    /// update, so that its foot, losing its other half, breaks too.
    /// </summary>
    internal async ValueTask BreakStrawBedAsync(Vector head)
    {
        this.BlockUpdates.PlaySound(head, "minecraft:block.straw_bed.break_leave");
        await this.SetBlockAsync(head, BlocksRegistry.Air, BlockUpdateFlags.All);
    }

    // Vanilla's BedBlock.destroyOnUse: both halves removed, then an explosion that sets fires at the head.
    private async ValueTask ExplodeBedAsync(Vector head, IBlock headBlock)
    {
        await SetBlockAsync(head, BlocksRegistry.Air, true);

        var foot = head.Offset(BedBlocks.Facing(headBlock).Opposite());
        if (GameEventBlock(foot)?.RegistryId == headBlock.RegistryId)
            await SetBlockAsync(foot, BlocksRegistry.Air, true);

        await ExplodeAsync(
            (VectorD)head + new VectorD(0.5, 0.5, 0.5),
            BadRespawnPointPower,
            fire: true,
            ExplosionInteraction.Block);
    }

    /// <summary>
    /// A player using a respawn anchor, like vanilla's <c>RespawnAnchorBlock</c>: glowstone charges it up to four
    /// times; a charged anchor where <c>respawn_anchor_works</c> is false (everywhere but the nether) is removed and
    /// explodes.
    /// </summary>
    /// <remarks>
    /// A working anchor sets the player's spawn. Unlike vanilla, an anchor next to water explodes like any other.
    /// </remarks>
    /// <returns>
    /// Whether the use is done with; an empty anchor, the offhand without glowstone, and the main hand while the
    /// offhand's glowstone can charge the anchor let the held item be used instead.
    /// </returns>
    internal async ValueTask<bool> UseRespawnAnchorAsync(IPlayer player, Vector position, IBlock block, int handSlot)
    {
        if (!block.Is(BlockIds.RespawnAnchor))
            return false;

        var charges = int.TryParse(block.GetProperty("charges"), out var parsed) ? parsed : 0;
        var held = player.Inventory.GetItem(handSlot);

        if (held is { Count: > 0, Holder.Id: ItemIds.Glowstone } && charges < MaxAnchorCharges)
        {
            await SetBlockAsync(position, block.WithProperty("charges", charges + 1), true);

            var center = (VectorD)position + new VectorD(0.5, 0.5, 0.5);
            PacketBroadcaster.QueuePacketToLevelInRange(this, center, new SoundPacket
            {
                SoundLocation = "minecraft:block.respawn_anchor.charge",
                SoundPosition = new SoundPosition(center.X, center.Y, center.Z),
                Category = SoundCategory.Blocks,
                Volume = 1,
                Pitch = 1,
                Seed = Random.Shared.NextInt64()
            });

            if (player.GameMode != GameMode.Creative)
            {
                player.Inventory.RemoveItem(handSlot, 1);
                await player.QueuePacketAsync(new ContainerSetSlotPacket
                {
                    ContainerId = 0,
                    Slot = (short)handSlot,
                    SlotData = player.Inventory.GetItem(handSlot)
                });
            }

            return true;
        }

        // Like vanilla's useItemOn and ServerPlayerGameMode: only the main hand uses the anchor empty-handed, and not
        // while glowstone in the offhand could charge it instead, so the offhand's use that follows does. Without
        // this, a pickaxe in the main hand would explode a partly charged anchor that offhand glowstone should charge.
        if (handSlot == OffhandSlot)
            return false;

        var offhand = player.Inventory.GetItem(OffhandSlot);
        if (offhand is { Count: > 0, Holder.Id: ItemIds.Glowstone } && charges < MaxAnchorCharges)
            return false;

        if (charges == 0)
            return false;

        if (!this.Gameplay(LevelEnvironment.RespawnAnchorWorks, position))
        {
            await SetBlockAsync(position, BlocksRegistry.Air, true);
            await ExplodeAsync(
                (VectorD)position + new VectorD(0.5, 0.5, 0.5),
                BadRespawnPointPower,
                fire: true,
                ExplosionInteraction.Block);
        }
        else if (player is PlayerBase owner)
        {
            // Like vanilla's RespawnAnchorBlock.useWithoutItem: a working anchor sets the player's spawn.
            await owner.SetRespawnPointAsync(
                new RespawnPoint(this.DimensionName, position, player.Yaw, Forced: false), announce: true);
        }

        return true;
    }

    /// <summary>
    /// Whether water poured at <paramref name="position"/> evaporates, like vanilla's <c>BucketItem.emptyContents</c>
    /// where <c>water_evaporates</c> is true (the nether): it isn't placed, and the players around hear it hiss.
    /// </summary>
    internal bool Evaporates(IPlayer player, Vector position)
    {
        if (!this.Gameplay(LevelEnvironment.WaterEvaporates, position))
            return false;

        // Vanilla's client plays the sound for the player who poured it, so the server sends it to the others.
        var center = (VectorD)position + new VectorD(0.5, 0.5, 0.5);
        PacketBroadcaster.QueuePacketToLevelInRange(this, center, new SoundPacket
        {
            SoundLocation = "minecraft:block.fire.extinguish",
            SoundPosition = new SoundPosition(center.X, center.Y, center.Z),
            Category = SoundCategory.Blocks,
            Volume = 0.5f,
            Pitch = 2.6f + (Random.Shared.NextSingle() - Random.Shared.NextSingle()) * 0.8f,
            Seed = Random.Shared.NextInt64()
        }, player.EntityId);

        PacketBroadcaster.QueuePacketToLevelInRange(this, center, new LevelParticlesPacket
        {
            Position = position,
            Offset = new VectorF(1, 1, 1),
            ParticleCount = 8,
            Randomization = ParticleRandomization.Alternative,
            Data = new BucketSteamParticle()
        });

        return true;
    }

    private sealed class BucketSteamParticle : ParticleData
    {
        public override ParticleType ParticleType => ParticleType.LargeSmoke;
    }
}
