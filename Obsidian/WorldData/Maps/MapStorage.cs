using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using Obsidian.Nbt;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData.Maps;

/// <summary>
/// The maps of a world and its dimensions, saved like vanilla's in the world's <c>data</c> folder: <c>map_&lt;id&gt;.dat</c>
/// per map and the last id in <c>idcounts.dat</c>.
/// </summary>
internal sealed class MapStorage(string folderPath)
{
    // Obsidian's saved data version, as in its regions.
    private const int DataVersion = 3337;

    private readonly ConcurrentDictionary<int, MapData?> maps = new();
    private readonly Lock idLock = new();
    private int? lastMapId;

    private string DataPath => Path.Combine(folderPath, "data");

    /// <summary>
    /// The storage holding the maps of <paramref name="level"/>: its world's, since vanilla keeps every dimension's maps in
    /// the overworld.
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
    public async ValueTask TickAsync(Player player)
    {
        var level = (AbstractLevel)player.Level;
        var inventory = player.Inventory;
        for (var slot = 0; slot < inventory.Size; slot++)
        {
            var stack = inventory.GetItem(slot);
            var mapId = stack is not null && stack.Type == Material.FilledMap ? GetMapId(stack) : null;
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
                player.Client.SendPacket(packet);
        }
    }

    /// <summary>
    /// Writes the maps that changed and the last map id.
    /// </summary>
    public async Task SaveAsync()
    {
        Directory.CreateDirectory(this.DataPath);

        foreach (var (id, data) in this.maps)
        {
            if (data is null || !data.IsDirty)
                continue;

            data.IsDirty = false;
            await WriteAsync(Path.Combine(this.DataPath, $"map_{id}.dat"), data.ToNbt());
        }

        int? lastId;
        lock (this.idLock)
            lastId = this.lastMapId;

        if (lastId is not null)
            await WriteAsync(Path.Combine(this.DataPath, "idcounts.dat"), new NbtCompound("data") { new NbtTag<int>("map", lastId.Value) });
    }

    private MapData? Load(int id)
    {
        var data = Read(Path.Combine(this.DataPath, $"map_{id}.dat"));
        return data is null ? null : MapData.FromNbt(data);
    }

    private int LoadLastMapId()
    {
        var data = Read(Path.Combine(this.DataPath, "idcounts.dat"));
        return data is not null && data.TryGetTag<NbtTag<int>>("map", out var map) ? map.Value : -1;
    }

    /// <summary>
    /// The <c>data</c> compound of a saved data file, or <c>null</c> when there's none.
    /// </summary>
    private static NbtCompound? Read(string path)
    {
        if (!File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        var root = new NbtReader(stream, NbtCompression.GZip).ReadNextTag() as NbtCompound;
        return root is not null && root.TryGetTag<NbtCompound>("data", out var data) ? data : null;
    }

    private static async Task WriteAsync(string path, NbtCompound data)
    {
        await using var stream = File.Create(path);
        await using var writer = new NbtWriterStream(stream, NbtCompression.GZip, "");
        writer.WriteTag(data);
        writer.WriteInt("DataVersion", DataVersion);
        writer.EndCompound();
        await writer.TryFinishAsync();
    }
}
