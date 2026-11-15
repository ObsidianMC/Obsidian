namespace Obsidian.API;

/// <summary>How a text display turns to face the viewer, like vanilla's <c>Display.BillboardConstraints</c>.</summary>
public enum TextDisplayBillboard : byte
{
    /// <summary>The display keeps its own rotation.</summary>
    Fixed,
    /// <summary>The display turns left and right to face the viewer but keeps its pitch.</summary>
    Vertical,
    /// <summary>The display tilts up and down to face the viewer but keeps its yaw.</summary>
    Horizontal,
    /// <summary>The display always faces the viewer.</summary>
    Center
}
