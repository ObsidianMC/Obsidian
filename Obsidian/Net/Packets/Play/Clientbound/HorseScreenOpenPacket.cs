using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Sent to tell the client to open the horse GUI. Opening other GUIs are done via <see cref="OpenScreenPacket"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="HorseScreenOpenPacket"/> class.
/// </remarks>
/// <param name="windowId">The identifier of the window to open.</param>
/// <param name="columnCount">How many columns the horse inventory should have.</param>
/// <param name="entityId">The owner entity of the window.</param>
public partial class HorseScreenOpenPacket(int windowId, int columnCount, int entityId)
{
    /// <summary>
    /// The identifier of the window to open.
    /// </summary>
    [Field(0), VarLength]
    public int WindowID { get; set; } = windowId;

    /// <summary>
    /// How many columns the horse inventory should have.
    /// </summary>
    [Field(1), VarLength]
    public int ColumnCount { get; set; } = columnCount;

    /// <summary>
    /// The owner entity of the window.
    /// </summary>
    [Field(2)]
    public int EntityID { get; set; } = entityId;

    public override void Serialize(INetStreamWriter writer) 
    {
        writer.WriteVarInt(WindowID);
        writer.WriteVarInt(ColumnCount);
        writer.WriteInt(EntityID);
    }
}
