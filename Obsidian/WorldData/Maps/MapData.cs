using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian.WorldData.Maps;

/// <summary>
/// A map's saved data, like vanilla's <c>MapItemSavedData</c>: what it shows (128x128 color bytes around its center) and the
/// decorations drawn on it, plus what each player carrying it was last sent.
/// </summary>
/// <remarks>
/// Banner markers and maps in item frames aren't supported yet.
/// </remarks>
internal sealed class MapData
{
    public const int Size = 128;

    // Players carrying the map tick it from several dimensions at once.
    private readonly Lock sync = new();
    private readonly OrderedDictionary<string, MapIcon> decorations = [];
    private readonly List<HoldingPlayer> carriedBy = [];

    private MapData(int centerX, int centerZ, byte scale, bool trackingPosition, bool unlimitedTracking, bool locked, string dimension)
    {
        this.CenterX = centerX;
        this.CenterZ = centerZ;
        this.Scale = scale;
        this.TrackingPosition = trackingPosition;
        this.UnlimitedTracking = unlimitedTracking;
        this.Locked = locked;
        this.Dimension = dimension;
    }

    public int CenterX { get; }

    public int CenterZ { get; }

    /// <summary>
    /// Each pixel covers <c>2^Scale</c> blocks a side.
    /// </summary>
    public byte Scale { get; }

    /// <summary>
    /// The dimension the map shows, e.g. <c>minecraft:overworld</c>.
    /// </summary>
    public string Dimension { get; }

    /// <summary>
    /// Whether players carrying the map are marked on it.
    /// </summary>
    public bool TrackingPosition { get; }

    /// <summary>
    /// Whether players and decorations far outside the map are still marked at its edge (explorer maps).
    /// </summary>
    public bool UnlimitedTracking { get; }

    public bool Locked { get; }

    /// <summary>
    /// The color bytes (<see cref="MapColors.Pack"/>), row by row (<c>x + z * 128</c>).
    /// </summary>
    public byte[] Colors { get; private set; } = new byte[Size * Size];

    /// <summary>
    /// Whether the colors changed since the map was last saved.
    /// </summary>
    public bool IsDirty { get; set; }

    /// <summary>
    /// Vanilla <c>createFresh</c>: a blank map whose grid cell contains the position.
    /// </summary>
    public static MapData CreateFresh(double x, double z, byte scale, bool trackingPosition, bool unlimitedTracking, string dimension)
    {
        var size = Size * (1 << scale);
        var cellX = (int)Math.Floor((x + 64.0) / size);
        var cellZ = (int)Math.Floor((z + 64.0) / size);
        return new MapData(cellX * size + size / 2 - 64, cellZ * size + size / 2 - 64, scale, trackingPosition, unlimitedTracking, false,
            dimension) { IsDirty = true };
    }

    /// <summary>
    /// Vanilla <c>updateColor</c>: sets a pixel, returning whether it changed.
    /// </summary>
    public bool UpdateColor(int x, int z, byte color)
    {
        lock (this.sync)
        {
            if (this.Colors[x + z * Size] == color)
                return false;

            this.SetColor(x, z, color);
            return true;
        }
    }

    /// <summary>
    /// Vanilla <c>setColor</c>: sets a pixel and marks it for every player carrying the map.
    /// </summary>
    public void SetColor(int x, int z, byte color)
    {
        lock (this.sync)
        {
            this.Colors[x + z * Size] = color;
            this.IsDirty = true;
            foreach (var holder in this.carriedBy)
                holder.MarkColorsDirty(x, z);
        }
    }

    /// <summary>
    /// The holding state of a player carrying the map, created on first use.
    /// </summary>
    public HoldingPlayer GetHoldingPlayer(Player player)
    {
        lock (this.sync)
        {
            var holder = this.carriedBy.Find(holder => holder.Player == player);
            if (holder is null)
                this.carriedBy.Add(holder = new HoldingPlayer(this, player));

            return holder;
        }
    }

    /// <summary>
    /// Vanilla <c>tickCarriedBy</c>: updates the markers of the players carrying the map, dropping those who no longer do, and
    /// adds the decorations stored on the item (explorer map targets).
    /// </summary>
    public void TickCarriedBy(Player player, ItemStack stack, int mapId, long gameTime)
    {
        lock (this.sync)
        {
            this.GetHoldingPlayer(player);

            if (!Carries(player, mapId))
                this.RemoveDecoration(player.Username);

            for (var i = 0; i < this.carriedBy.Count; i++)
            {
                var holder = this.carriedBy[i];
                var other = holder.Player;
                if (other.Level.Players.ContainsKey(other.Uuid) && Carries(other, mapId))
                {
                    if (((AbstractLevel)other.Level).DimensionName == this.Dimension && this.TrackingPosition)
                        this.AddDecoration(MapDecorationType.Player, gameTime, other.Username, other.Position.X, other.Position.Z, other.Yaw, null);
                }
                else
                {
                    this.carriedBy.RemoveAt(i--);
                    this.RemoveDecoration(other.Username);
                }
            }

            var stored = stack.GetComponent<MapDecorationDataComponent>(DataComponentType.MapDecorations);
            if (stored is not null)
            {
                foreach (var (key, entry) in stored.Decorations)
                {
                    if (!this.decorations.ContainsKey(key))
                        this.AddDecoration(entry.Type, gameTime, key, entry.X, entry.Z, entry.Rotation, null);
                }
            }
        }
    }

    /// <summary>
    /// Vanilla <c>getUpdatePacket</c>: the changed colors and (every 5 ticks) decorations to send a carrying player, or
    /// <c>null</c> when there's nothing new.
    /// </summary>
    public MapItemDataPacket? GetUpdatePacket(int mapId, Player player)
    {
        lock (this.sync)
            return this.carriedBy.Find(holder => holder.Player == player)?.NextUpdatePacket(mapId);
    }

    public NbtCompound ToNbt() => new("data")
    {
        new NbtTag<string>("dimension", this.Dimension),
        new NbtTag<int>("xCenter", this.CenterX),
        new NbtTag<int>("zCenter", this.CenterZ),
        new NbtTag<byte>("scale", this.Scale),
        new NbtArray<byte>("colors", [.. this.Colors]),
        new NbtTag<bool>("trackingPosition", this.TrackingPosition),
        new NbtTag<bool>("unlimitedTracking", this.UnlimitedTracking),
        new NbtTag<bool>("locked", this.Locked)
    };

    public static MapData FromNbt(NbtCompound data)
    {
        var map = new MapData(
            data.TryGetTag<NbtTag<int>>("xCenter", out var centerX) ? centerX.Value : 0,
            data.TryGetTag<NbtTag<int>>("zCenter", out var centerZ) ? centerZ.Value : 0,
            data.TryGetTag<NbtTag<byte>>("scale", out var scale) ? (byte)Math.Clamp((int)scale.Value, 0, 4) : (byte)0,
            !data.TryGetTag<NbtTag<bool>>("trackingPosition", out var tracking) || tracking.Value,
            data.TryGetTag<NbtTag<bool>>("unlimitedTracking", out var unlimited) && unlimited.Value,
            data.TryGetTag<NbtTag<bool>>("locked", out var locked) && locked.Value,
            data.TryGetTag<NbtTag<string>>("dimension", out var dimension) ? dimension.Value! : "minecraft:overworld");

        if (data.TryGetTag<NbtArray<byte>>("colors", out var colors) && colors.Count == Size * Size)
            map.Colors = [.. colors.GetArray()];

        return map;
    }

    private static bool Carries(Player player, int mapId)
    {
        var inventory = player.Inventory;
        for (var slot = 0; slot < inventory.Size; slot++)
        {
            var stack = inventory.GetItem(slot);
            if (stack is not null && stack.Type == Material.FilledMap && MapStorage.GetMapId(stack) == mapId)
                return true;
        }

        return false;
    }

    private void RemoveDecoration(string key)
    {
        this.decorations.Remove(key);
        this.SetDecorationsDirty();
    }

    /// <summary>
    /// Vanilla <c>addDecoration</c>: places a decoration in map coordinates, or removes it when it's off the map.
    /// </summary>
    private void AddDecoration(MapDecorationType type, long gameTime, string key, double x, double z, double rotation, ChatMessage? name)
    {
        var scale = 1 << this.Scale;
        var mapX = (float)(x - this.CenterX) / scale;
        var mapZ = (float)(z - this.CenterZ) / scale;
        var icon = this.CalculateDecoration(type, gameTime, rotation, mapX, mapZ, name);
        if (icon is null)
        {
            this.RemoveDecoration(key);
            return;
        }

        if (this.decorations.TryGetValue(key, out var previous) && previous == icon)
            return;

        this.decorations[key] = icon;
        this.SetDecorationsDirty();
    }

    /// <summary>
    /// Vanilla <c>calculateDecorationLocationAndType</c>: players outside the map become edge markers, other decorations
    /// outside it vanish unless the map tracks without limit.
    /// </summary>
    private MapIcon? CalculateDecoration(MapDecorationType type, long gameTime, double rotation, float mapX, float mapZ, ChatMessage? name)
    {
        var x = ClampMapCoordinate(mapX);
        var z = ClampMapCoordinate(mapZ);
        if (type == MapDecorationType.Player)
        {
            if (IsInsideMap(mapX, mapZ))
                return new MapIcon(type, x, z, this.CalculateRotation(gameTime, rotation), name);

            var outside = Math.Abs(mapX) < 320.0f && Math.Abs(mapZ) < 320.0f ? MapDecorationType.PlayerOffMap
                : this.UnlimitedTracking ? MapDecorationType.PlayerOffLimits : (MapDecorationType?)null;
            return outside is null ? null : new MapIcon(outside.Value, x, z, 0, name);
        }

        return !IsInsideMap(mapX, mapZ) && !this.UnlimitedTracking ? null : new MapIcon(type, x, z, this.CalculateRotation(gameTime, rotation), name);
    }

    private byte CalculateRotation(long gameTime, double rotation)
    {
        // Needles spin in the nether.
        if (this.Dimension == "minecraft:the_nether")
        {
            var time = (int)(gameTime / 10L);
            return (byte)((time * time * 34187121 + time * 121) >> 15 & 15);
        }

        var shifted = rotation < 0.0 ? rotation - 8.0 : rotation + 8.0;
        return (byte)((int)(shifted * 16.0 / 360.0) & 15);
    }

    private static bool IsInsideMap(float x, float z) => x >= -63.0f && z >= -63.0f && x <= 63.0f && z <= 63.0f;

    private static sbyte ClampMapCoordinate(float value) => value <= -63.0f ? (sbyte)-128 : value >= 63.0f ? (sbyte)127 : (sbyte)(value * 2.0f + 0.5);

    private void SetDecorationsDirty()
    {
        foreach (var holder in this.carriedBy)
            holder.MarkDecorationsDirty();
    }

    /// <summary>
    /// A decoration as drawn on the map, like vanilla's <c>MapDecoration</c>: map coordinates (-128 to 127) and a rotation in
    /// sixteenths of a turn.
    /// </summary>
    public sealed record MapIcon(MapDecorationType Type, sbyte X, sbyte Z, byte Rotation, ChatMessage? Name);

    /// <summary>
    /// What a player carrying the map still has to be sent, like vanilla's <c>MapItemSavedData.HoldingPlayer</c>.
    /// </summary>
    public sealed class HoldingPlayer(MapData map, Player player)
    {
        private bool dirtyData = true;
        private int minDirtyX;
        private int minDirtyZ;
        private int maxDirtyX = Size - 1;
        private int maxDirtyZ = Size - 1;
        private bool dirtyDecorations = true;
        private int tick;

        public Player Player { get; } = player;

        /// <summary>
        /// Which column band the map renders next while held (vanilla's <c>step</c>).
        /// </summary>
        public int Step { get; set; }

        public void MarkColorsDirty(int x, int z)
        {
            if (this.dirtyData)
            {
                this.minDirtyX = Math.Min(this.minDirtyX, x);
                this.minDirtyZ = Math.Min(this.minDirtyZ, z);
                this.maxDirtyX = Math.Max(this.maxDirtyX, x);
                this.maxDirtyZ = Math.Max(this.maxDirtyZ, z);
                return;
            }

            this.dirtyData = true;
            (this.minDirtyX, this.minDirtyZ, this.maxDirtyX, this.maxDirtyZ) = (x, z, x, z);
        }

        public void MarkDecorationsDirty() => this.dirtyDecorations = true;

        public MapItemDataPacket? NextUpdatePacket(int mapId)
        {
            MapPatch? patch = null;
            if (this.dirtyData)
            {
                this.dirtyData = false;
                patch = this.CreatePatch();
            }

            List<MapIcon>? icons = null;
            if (this.dirtyDecorations && this.tick++ % 5 == 0)
            {
                this.dirtyDecorations = false;
                icons = [.. map.decorations.Values];
            }

            return patch is null && icons is null ? null : new MapItemDataPacket(mapId, map.Scale, map.Locked, icons, patch);
        }

        private MapPatch CreatePatch()
        {
            var width = this.maxDirtyX + 1 - this.minDirtyX;
            var height = this.maxDirtyZ + 1 - this.minDirtyZ;
            var colors = new byte[width * height];
            for (var x = 0; x < width; x++)
            {
                for (var z = 0; z < height; z++)
                    colors[x + z * width] = map.Colors[this.minDirtyX + x + (this.minDirtyZ + z) * Size];
            }

            return new MapPatch(this.minDirtyX, this.minDirtyZ, width, height, colors);
        }
    }
}

/// <summary>
/// A rectangle of map colors sent to a client, like vanilla's <c>MapItemSavedData.MapPatch</c>.
/// </summary>
internal sealed record MapPatch(int StartX, int StartZ, int Width, int Height, byte[] Colors);
