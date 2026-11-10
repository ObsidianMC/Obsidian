using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla's JukeboxBlockEntity saves how far into its song a playing jukebox is; one that isn't playing saves none.
    internal const string SongTicksField = "ticks_since_song_started";

    // What Obsidian saved before it used vanilla's field, still read from older worlds.
    private const string LegacyPlayingField = "IsPlaying";
    private const string LegacyElapsedField = "ObsidianSongElapsed";

    private readonly ConcurrentDictionary<Vector, int> playingJukeboxes = [];
    internal void StartJukebox(Vector point, int song, int elapsed = 0)
    {
        if ((uint)song >= JukeboxSongsRegistry.Songs.Length)
            return;
        var remaining = JukeboxSongsRegistry.Songs[song].Seconds * 20 + 20 - elapsed;
        if (remaining <= 0)
            return;
        playingJukeboxes[point] = remaining;
        // Like vanilla's JukeboxSongPlayer, the song's game event comes from its tick, not as it starts.
        PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new LevelEventPacket(1010, point, song));
    }

    /// <summary>
    /// Vanilla's <c>JukeboxSongPlayer.stop</c>: a jukebox playing a song stops, which the players around hear (level
    /// event 1011) and listeners notice (<c>jukebox_stop_play</c>). One that isn't playing does nothing.
    /// </summary>
    /// <param name="point">The jukebox's position.</param>
    /// <param name="jukebox">Its block entity, when it's no longer in its chunk.</param>
    internal void StopJukebox(Vector point, DataBlockEntity? jukebox = null)
    {
        playingJukeboxes.TryRemove(point, out _);
        var chunk = GetLoadedChunk(point.X >> 4, point.Z >> 4);
        jukebox ??= chunk?.GetBlockEntity(point.X, point.Y, point.Z) as DataBlockEntity;
        if (jukebox is null || SongTicks(jukebox) is null)
            return;

        jukebox.Remove(SongTicksField);
        PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new LevelEventPacket(1011, point, 0));
        EmitGameEvent(MobGameEvent.JukeboxStop, (VectorD)point + new VectorD(0.5, 0.5, 0.5));
    }

    /// <summary>
    /// Vanilla's <c>JukeboxBlockEntity.popOutTheItem</c>: the song stops and the disc pops out on top of the jukebox,
    /// pushed a little to the side, and can be picked up after vanilla's default delay.
    /// </summary>
    internal void EjectRecord(Vector point, ItemStack disc, DataBlockEntity? jukebox = null)
    {
        this.StopJukebox(point, jukebox);

        var motion = new VectorD(
            (Random.Shared.NextDouble() - 0.5) * 0.2,
            0.2,
            (Random.Shared.NextDouble() - 0.5) * 0.2);
        this.SpawnEntity(new ItemEntity
        {
            Level = this,
            EntityId = Obsidian.Server.GetNextEntityId(),
            Position = (VectorD)point + new VectorD(0.5, 1, 0.5),
            Item = disc,
            Motion = motion,
            CanPickup = false
        });
    }

    /// <summary>
    /// What removing a jukebox does, like vanilla's <c>JukeboxBlockEntity</c>: <c>preRemoveSideEffects</c> pops its
    /// disc out, and <c>setRemoved</c> then stops it once more, whether it played or not.
    /// </summary>
    private void RemoveJukebox(DataBlockEntity jukebox)
    {
        var point = jukebox.BlockPosition;
        ItemStack? disc;
        lock (jukebox.Data)
        {
            disc = jukebox.SnapshotTag<NbtCompound>("RecordItem")?.ItemFromNbt();
            jukebox.Remove("RecordItem");
        }

        if (disc is { IsAir: false, Count: > 0 })
            this.EjectRecord(point, disc, jukebox);

        playingJukeboxes.TryRemove(point, out _);
        PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new LevelEventPacket(1011, point, 0));
        EmitGameEvent(MobGameEvent.JukeboxStop, (VectorD)point + new VectorD(0.5, 0.5, 0.5));
    }

    private void RegisterChunkMusic(Chunk chunk)
    {
        if (chunk.MusicRegistered)
            return;
        chunk.MusicRegistered = true;
        foreach (var entity in chunk.GetBlockEntities().OfType<DataBlockEntity>())
        {
            if (entity.Id is "minecraft:sculk_sensor" or "minecraft:calibrated_sculk_sensor" or "minecraft:sculk_shrieker")
            {
                var block = GameEventBlock(entity.BlockPosition);
                if (block?.GetProperty("shrieking") == "true")
                    activeSculk.TryAdd(entity.BlockPosition, 90);
                else if (block?.GetProperty("sculk_sensor_phase") is "active" or "cooldown")
                    activeSculk.TryAdd(entity.BlockPosition, 10);
            }
            else if (entity.Id == "minecraft:jukebox")
            {
                if (SongTicks(entity) is long elapsed
                    && entity.SnapshotTag<NbtCompound>("RecordItem")?.ItemFromNbt() is { } stack)
                {
                    StartJukebox(entity.BlockPosition, JukeboxSongsRegistry.GetSong(stack), (int)elapsed);
                }
            }
        }
    }

    private void TickJukeboxes()
    {
        foreach (var (point, remaining) in playingJukeboxes.ToArray())
        {
            var chunk = GetLoadedChunk(point.X >> 4, point.Z >> 4);
            if (chunk == null)
            {
                playingJukeboxes.TryRemove(point, out _);
                continue;
            }
            if (GameEventBlock(point)?.RegistryId != BlockIds.Jukebox || remaining <= 1)
            {
                StopJukebox(point);
                continue;
            }

            if (!IsMobTicking((VectorD)point))
                continue;
            playingJukeboxes[point] = remaining - 1;
            if (chunk.GetBlockEntity(point.X, point.Y, point.Z) is DataBlockEntity jukebox)
                jukebox.Set(SongTicksField, (SongTicks(jukebox) ?? 0) + 1);
            if (remaining % 20 == 0)
                EmitGameEvent(MobGameEvent.JukeboxPlay, (VectorD)point + new VectorD(0.5, 0.5, 0.5));
        }
    }

    /// <summary>
    /// How far into its song a jukebox is, in ticks, or null when it isn't playing: vanilla's
    /// <c>ticks_since_song_started</c>, or what Obsidian saved before, which is moved to vanilla's field.
    /// </summary>
    internal static long? SongTicks(DataBlockEntity jukebox)
    {
        var data = jukebox.SnapshotData();
        if (data.TryGetNumber<long>(SongTicksField, out var ticks))
            return ticks;

        if (!data.TryGetBool(LegacyPlayingField, out var playing))
            return null;

        jukebox.Remove(LegacyPlayingField);
        jukebox.Remove(LegacyElapsedField);
        if (!playing)
            return null;

        long elapsed = data.TryGetTagValue<int>(LegacyElapsedField, out var saved) ? saved : 0;
        jukebox.Set(SongTicksField, elapsed);
        return elapsed;
    }

    internal void PlayNoteBlock(Vector point, IEntity? source = null)
    {
        lock (this.Fluids.TickLock)
        {
            var block = GameEventBlock(point);
            if (block?.RegistryId != BlockIds.NoteBlock)
                return;

            var instrument = NoteBlockBehavior.GetInstrument(block);
            if (!instrument.Above && GameEventBlock(point + Vector.Up)?.IsAir != true)
                return;

            // Each activation emits a game event even when its pending block event is coalesced.
            this.QueueBlockEvent(point, BlockIds.NoteBlock, 0, 0);

            EmitGameEvent(MobGameEvent.NoteBlockPlay, (VectorD)point + new VectorD(0.5, 0.5, 0.5), source);
        }
    }

    private bool TriggerNoteBlockEvent(Vector point)
    {
        lock (this.Fluids.TickLock)
        {
            var block = GameEventBlock(point);
            if (block?.RegistryId != BlockIds.NoteBlock)
                return false;

            // Vanilla reads the current state and draws a seed only for successful playback.
            var instrument = NoteBlockBehavior.GetInstrument(block);
            if (instrument.Custom)
                return false;

            var note = int.TryParse(block.GetProperty("note"), out var number) ? number : 0;
            PacketBroadcaster.BroadcastToLevelInRange(this, (VectorD)point, new SoundPacket
            {
                SoundLocation = instrument.Sound,
                Category = SoundCategory.Records,
                SoundPosition = new SoundPosition(point.X + 0.5, point.Y + 0.5, point.Z + 0.5),
                Volume = 3,
                Pitch = instrument.Tunable ? (float)Math.Pow(2, (note - 12) / 12d) : 1,
                Seed = this.GrowthRandom.NextLong()
            });

            return true;
        }
    }
}
