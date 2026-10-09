using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    private async ValueTask<bool> TryUseMusicBlockAsync(IPlayer player, int slot, ItemStack? held, IBlock block)
    {
        if (block.Material is not Material.NoteBlock and not Material.Jukebox || player.Sneaking || player.Level is not AbstractLevel level) return false;
        if (player.Health <= 0 || player.GameMode is GameMode.Spectator or GameMode.Adventure || (player.Position - (VectorD)Position).MagnitudeSquared() > 36) return true;
        if (block.Material == Material.NoteBlock)
        {
            var note = int.TryParse(block.GetProperty("note"), out var number) ? number : 0;
            await level.SetBlockAsync(Position, block.WithProperty("note", (note + 1) % 25), true);
            level.PlayNoteBlock(Position, player);
        }
        else if (level.GetLoadedChunk(Position.X >> 4, Position.Z >> 4)?.GetBlockEntity(Position.X, Position.Y, Position.Z) is DataBlockEntity jukebox)
        {
            var data = jukebox.SnapshotData();
            if (data.TryGetTag<NbtCompound>("RecordItem", out var record) && record.ItemFromNbt() is { } stack && !stack.IsAir)
            {
                level.StopJukebox(Position);
                lock (jukebox.Data) jukebox.Data.Remove("RecordItem");
                await level.SetBlockAsync(Position, block.WithProperty("has_record", false), true);
                level.SpawnEntity(new ItemEntity { Level = level, EntityId = Server.GetNextEntityId(), Position = (VectorD)Position + new VectorD(0.5, 1, 0.5), Item = stack,
                    Motion = new VectorD((Random.Shared.NextDouble() - 0.5) * 0.2, 0.2, (Random.Shared.NextDouble() - 0.5) * 0.2), CanPickup = false });
            }
            else if (held is { Count: > 0 } && JukeboxSongsRegistry.GetSong(held) is var song && song >= 0)
            {
                jukebox.Set(new ItemStack(held, 1).ToNbt("RecordItem"));
                jukebox.Set("IsPlaying", true);
                jukebox.Set("ObsidianSongElapsed", 0);
                await level.SetBlockAsync(Position, block.WithProperty("has_record", true), true);
                level.StartJukebox(Position, song);
                if (player.GameMode != GameMode.Creative)
                {
                    player.Inventory.RemoveItem(slot, 1);
                    await player.Client.QueuePacketAsync(new ContainerSetSlotPacket { ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot) });
                }
            }
        }
        player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = Sequence });
        return true;
    }
}
