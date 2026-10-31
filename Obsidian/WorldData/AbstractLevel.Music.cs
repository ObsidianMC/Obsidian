using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private readonly ConcurrentDictionary<Vector, int> playingJukeboxes = [];
    internal void StartJukebox(Vector point, int song, int elapsed = 0)
    {
        if ((uint)song >= JukeboxSongsRegistry.Songs.Length)
            return;
        var remaining = JukeboxSongsRegistry.Songs[song].Seconds * 20 + 20 - elapsed;
        if (remaining <= 0)
            return;
        playingJukeboxes[point] = remaining;
        PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new LevelEventPacket(1010, point, song));
        EmitGameEvent(MobGameEvent.JukeboxPlay, (VectorD)point + new VectorD(0.5, 0.5, 0.5));
    }

    internal void StopJukebox(Vector point)
    {
        playingJukeboxes.TryRemove(point, out _);
        if (GetLoadedChunk(point.X >> 4, point.Z >> 4)?.GetBlockEntity(point.X, point.Y, point.Z) is DataBlockEntity jukebox)
            jukebox.Set("IsPlaying", false);
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
                var data = entity.SnapshotData();
                if (data.TryGetBool("IsPlaying", out var playing) && playing && data.TryGetTag<NbtCompound>("RecordItem", out var record) && record.ItemFromNbt() is { } stack)
                    StartJukebox(entity.BlockPosition, JukeboxSongsRegistry.GetSong(stack), data.TryGetTagValue<int>("ObsidianSongElapsed", out var elapsed) ? elapsed : 0);
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
            {
                var data = jukebox.SnapshotData();
                jukebox.Set("ObsidianSongElapsed", (data.TryGetTagValue<int>("ObsidianSongElapsed", out var elapsed) ? elapsed : 0) + 1);
            }
            if (remaining % 20 == 0)
                EmitGameEvent(MobGameEvent.JukeboxPlay, (VectorD)point + new VectorD(0.5, 0.5, 0.5));
        }
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
