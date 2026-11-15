namespace Obsidian.API;
/// <summary>A step of a mouse drag across inventory slots, combined with the mouse button used.</summary>
public enum DraggingState
{
    /// <summary>A left-button drag started; it spreads the held stack evenly.</summary>
    StartLeft = 0,
    /// <summary>A right-button drag started; it puts one item in each slot.</summary>
    StartRight = 4,
    /// <summary>A middle-button drag started; it fills each slot with a full stack, in creative mode only.</summary>
    StartMiddle = 8,

    /// <summary>A slot was added to a left-button drag.</summary>
    AddSlotLeft = 1,
    /// <summary>A slot was added to a right-button drag.</summary>
    AddSlotRight = 5,
    /// <summary>A slot was added to a middle-button drag.</summary>
    AddSlotMiddle = 9,

    /// <summary>A left-button drag ended.</summary>
    EndLeft = 2,
    /// <summary>A right-button drag ended.</summary>
    EndRight = 6,
    /// <summary>A middle-button drag ended.</summary>
    EndMiddle = 10
}
