using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using Obsidian.Nbt;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData.Maps;

/// <summary>
/// The maps of a world and its dimensions, saved like vanilla's in the world's <c>data/minecraft/maps</c> folder:
/// <c>&lt;id&gt;.dat</c> per map and the last id in <c>last_id.dat</c>.
/// </summary>
internal sealed class MapStorage(string folderPath)
{
    private readonly ConcurrentDictionary<int, MapData?> maps = new();
    private readonly Lock idLock = new();
    private int? lastMapId;

    private string MapsPath => Path.Combine(folderPath, "data", "minecraft", "maps");

    /// <summary>
    /// The storage holding the maps of <paramref name="level"/>: its world's, since vanilla keeps every dimension's maps in
    /// the world's folder.
    /// </summary>
    public static MapStorage For(ILevel level) => level is Dimension dimension ? ((World)dimension.ParentWorld).Maps : ((World)level).Maps;

    /// <summary>
    /// The map id of a filled map, or <c>null</c> when it has none.
    /// </summary>
    public static int? GetMapId(ItemStack stack) => stack.GetComponent<SimpleDataComponent<int>>(DataComponentType.MapId)?.Value;

    /// <summary>
    /// Vanilla <c>getFreeMapId</c>: the next unused id, counting from 0.
    /// </summary>
    public int GetFreeMapId()
    {
        lock (this.idLock)
        {
            this.lastMapId ??= this.LoadLastMapId();
            return (int)++this.lastMapId;
        }
    }

    /// <summary>
    /// A map's data, loaded on first use, or <c>null</c> when there's no such map.
    /// </summary>
    public MapData? Get(int id) => this.maps.GetOrAdd(id, this.Load);

    public void Set(int id, MapData data) => this.maps[id] = data;

    /// <summary>
    /// Vanilla <c>MapItem.create</c>: a new filled map with fresh data around (<paramref name="x"/>, <paramref name="z"/>).
    /// </summary>
    public ItemStack CreateMap(int x, int z, byte scale, bool trackingPosition, bool unlimitedTracking, string dimension)
    {
        var id = this.GetFreeMapId();
        this.Set(id, MapData.CreateFresh(x, z, scale, trackingPosition, unlimitedTracking, dimension));
        return new ItemStack(ItemsRegistry.FilledMap, 1, ComponentBuilder.MapId with { Value = id });
    }

    /// <summary>
    /// Ticks the maps a player carries, like vanilla's <c>MapItem.inventoryTick</c>: their markers, the terrain under a held
    /// map, then the updates the player is due.
    /// </summary>
    public async ValueTask TickAsync(IPlayer player)
    {
        var level = (AbstractLevel)player.Level;
        var inventory = player.Inventory;
        for (var slot = 0; slot < inventory.Size; slot++)
        {
            var stack = inventory.GetItem(slot);
            var mapId = stack is not null && stack.Is(ItemsRegistry.FilledMap) ? GetMapId(stack) : null;
            if (mapId is null)
                continue;

            var id = mapId.Value;
            var data = this.Get(id);
            if (data is null)
                continue;

            data.TickCarriedBy(player, stack!, id, level.Time);
            if (!data.Locked && (slot == player.CurrentHeldItemSlot || slot == 45))
                await MapRenderer.UpdateAsync(level, player, data);

            var packet = data.GetUpdatePacket(id, player);
            if (packet is not null)
                player.SendPacket(packet);
        }
    }

    /// <summary>
    /// Writes the maps that changed and the last map id.
    /// </summary>
    public void Save()
    {
        foreach (var (id, data) in this.maps)
        {
            if (data is null || !data.IsDirty)
                continue;

            data.IsDirty = false;
            SavedDataFile.Write(Path.Combine(this.MapsPath, $"{id}.dat"), data.ToNbt());
        }

        int? lastId;
        lock (this.idLock)
            lastId = this.lastMapId;

        if (lastId is not null)
            SavedDataFile.Write(Path.Combine(this.MapsPath, "last_id.dat"), new NbtCompound("data") { new NbtTag<int>("map", lastId.Value) });
    }

    private MapData? Load(int id)
    {
        var data = SavedDataFile.Read(Path.Combine(this.MapsPath, $"{id}.dat"));
        return data is null ? null : MapData.FromNbt(data);
    }

    private int LoadLastMapId()
    {
        var data = SavedDataFile.Read(Path.Combine(this.MapsPath, "last_id.dat"));
        return data is not null && data.TryGetTag<NbtTag<int>>("map", out var map) ? map.Value : -1;
    }
}
