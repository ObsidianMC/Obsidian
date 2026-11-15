using Obsidian.API.Inventory;

namespace Obsidian.API.Advancements;

/// <summary>How an advancement is shown on the advancement screen and in toasts.</summary>
public sealed class AdvancementDisplay
{
    /// <summary>Creates a display.</summary>
    /// <param name="title">The title.</param>
    /// <param name="description">The description shown under the title.</param>
    /// <param name="icon">The item shown as the icon.</param>
    /// <param name="advancementFrameType">The frame drawn around the icon.</param>
    /// <param name="flags">The display options.</param>
    /// <param name="backgroundTexture">The tab background texture, used when <paramref name="flags"/> includes
    /// <see cref="AdvancementFlags.HasBackgroundTexture"/>.</param>
    /// <param name="xCoord">The icon's X position on the advancement screen.</param>
    /// <param name="yCoord">The icon's Y position on the advancement screen.</param>
    public AdvancementDisplay(
        ChatMessage title,
        ChatMessage description,
        Item icon,
        AdvancementFrameType advancementFrameType,
        AdvancementFlags flags,
        string backgroundTexture,
        float xCoord,
        float yCoord)
    {
        Title = title;
        Description = description;
        Icon = icon;
        AdvancementFrameType = advancementFrameType;
        Flags = flags;
        BackgroundTexture = backgroundTexture;
        XCoord = xCoord;
        YCoord = yCoord;
    }

    /// <summary>Gets the title.</summary>
    public ChatMessage Title { get; init; }

    /// <summary>Gets the description shown under the title.</summary>
    public ChatMessage Description { get; init; }

    /// <summary>Gets the item shown as the icon when <see cref="IconStack"/> is null.</summary>
    public Item Icon { get; init; }

    /// <summary>The complete icon stack, including data components when specified in JSON.</summary>
    public ItemStack? IconStack { get; init; }

    /// <summary>Gets whether completing the advancement is announced in chat.</summary>
    public bool AnnounceToChat { get; init; } = true;

    /// <summary>Gets the frame drawn around the icon.</summary>
    public AdvancementFrameType AdvancementFrameType { get; init; }

    /// <summary>Gets the display options.</summary>
    public AdvancementFlags Flags { get; init; }

    /// <summary>
    /// Gets the tab background texture, sent only when <see cref="Flags"/> includes
    /// <see cref="AdvancementFlags.HasBackgroundTexture"/>.
    /// </summary>
    public string BackgroundTexture { get; init; }

    /// <summary>Gets the icon's X position on the advancement screen.</summary>
    public float XCoord { get; init; }

    /// <summary>Gets the icon's Y position on the advancement screen.</summary>
    public float YCoord { get; init; }
}

/// <summary>Options for how an advancement is shown.</summary>
[Flags]
public enum AdvancementFlags
{
    /// <summary>The advancement is a tab root with a background texture.</summary>
    HasBackgroundTexture = 1,

    /// <summary>A toast pops up when the advancement is completed.</summary>
    ShowToast,

    /// <summary>The advancement stays hidden until it is completed.</summary>
    Hidden = 4
}

/// <summary>The frame drawn around an advancement's icon.</summary>
public enum AdvancementFrameType : int
{
    /// <summary>A plain frame, for an ordinary advancement.</summary>
    Task,

    /// <summary>A spiked frame, for a hard advancement.</summary>
    Challenge,

    /// <summary>A rounded frame, for a long-term goal.</summary>
    Goal
}
