using Obsidian.WorldData.Maps;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Updates a map's colors and decorations on the client, like vanilla's <c>ClientboundMapItemDataPacket</c>.
/// </summary>
/// <param name="Decorations">Every decoration of the map, or <c>null</c> to keep the client's.</param>
/// <param name="Patch">The colors that changed, or <c>null</c> when none did.</param>
public partial class MapItemDataPacket
{
    internal MapItemDataPacket(int mapId, byte scale, bool locked, IReadOnlyList<MapData.MapIcon>? decorations, MapPatch? patch)
    {
        this.MapId = mapId;
        this.Scale = scale;
        this.Locked = locked;
        this.Decorations = decorations;
        this.Patch = patch;
    }

    public int MapId { get; }

    public byte Scale { get; }

    public bool Locked { get; }

    internal IReadOnlyList<MapData.MapIcon>? Decorations { get; }

    internal MapPatch? Patch { get; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.MapId);
        writer.WriteByte(this.Scale);
        writer.WriteBoolean(this.Locked);

        writer.WriteBoolean(this.Decorations is not null);
        if (this.Decorations is not null)
        {
            writer.WriteVarInt(this.Decorations.Count);
            foreach (var icon in this.Decorations)
            {
                writer.WriteVarInt((int)icon.Type);
                writer.WriteByte(icon.X);
                writer.WriteByte(icon.Z);
                writer.WriteByte((byte)(icon.Rotation & 15));
                writer.WriteBoolean(icon.Name is not null);
                if (icon.Name is not null)
                    writer.WriteChat(icon.Name);
            }
        }

        // A width of 0 means no colors follow.
        if (this.Patch is null)
        {
            writer.WriteByte(0);
            return;
        }

        writer.WriteByte((byte)this.Patch.Width);
        writer.WriteByte((byte)this.Patch.Height);
        writer.WriteByte((byte)this.Patch.StartX);
        writer.WriteByte((byte)this.Patch.StartZ);
        writer.WriteVarInt(this.Patch.Colors.Length);
        writer.WriteByteArray(this.Patch.Colors);
    }
}
