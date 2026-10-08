using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private readonly ConcurrentDictionary<Vector, int> playingJukeboxes = [];

    internal void StartJukebox(Vector point, int song, int elapsed = 0)
    {
        if ((uint)song >= JukeboxSongsRegistry.Songs.Length) return;
        var remaining = JukeboxSongsRegistry.Songs[song].Seconds * 20 + 20 - elapsed;
        if (remaining <= 0) return;
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
        if (chunk.MusicRegistered) return;
        chunk.MusicRegistered = true;
        foreach (var entity in chunk.GetBlockEntities().OfType<DataBlockEntity>())
        {
            if (entity.Id is "minecraft:sculk_sensor" or "minecraft:calibrated_sculk_sensor" or "minecraft:sculk_shrieker")
            {
                var block = GameEventBlock(entity.BlockPosition);
                if (block?.GetProperty("shrieking") == "true") activeSculk.TryAdd(entity.BlockPosition, 90);
                else if (block?.GetProperty("sculk_sensor_phase") is "active" or "cooldown") activeSculk.TryAdd(entity.BlockPosition, 10);
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
            if (chunk == null) { playingJukeboxes.TryRemove(point, out _); continue; }
            if (GameEventBlock(point)?.Material != Material.Jukebox || remaining <= 1) { StopJukebox(point); continue; }
            if (!IsMobTicking((VectorD)point)) continue;
            playingJukeboxes[point] = remaining - 1;
            if (chunk.GetBlockEntity(point.X, point.Y, point.Z) is DataBlockEntity jukebox)
            {
                var data = jukebox.SnapshotData();
                jukebox.Set("ObsidianSongElapsed", (data.TryGetTagValue<int>("ObsidianSongElapsed", out var elapsed) ? elapsed : 0) + 1);
            }
            if (remaining % 20 == 0) EmitGameEvent(MobGameEvent.JukeboxPlay, (VectorD)point + new VectorD(0.5, 0.5, 0.5));
        }
    }

    internal void PlayNoteBlock(Vector point, IEntity? source = null)
    {
        var block = GameEventBlock(point);
        if (block?.Material != Material.NoteBlock || GameEventBlock(point + new Vector(0, 1, 0))?.IsAir != true) return;
        var note = int.TryParse(block.GetProperty("note"), out var number) ? number : 0;
        var instrument = block.GetProperty("instrument") ?? "harp";
        PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new SoundPacket { SoundLocation = "minecraft:block.note_block." + instrument,
            Category = SoundCategory.Records, SoundPosition = new SoundPosition(point.X + 0.5, point.Y + 0.5, point.Z + 0.5),
            Volume = 3, Pitch = (float)Math.Pow(2, (note - 12) / 12d), Seed = Random.Shared.NextInt64() });
        EmitGameEvent(MobGameEvent.NoteBlockPlay, (VectorD)point + new VectorD(0.5, 0.5, 0.5), source);
    }
}
