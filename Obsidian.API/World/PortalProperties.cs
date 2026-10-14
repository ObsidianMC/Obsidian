using Obsidian.API.Inventory;

namespace Obsidian.API.World;

/// <summary>A rectangular portal, registered during ConfigureRegistry.</summary>
/// <remarks>
/// PortalBlock must have an axis property accepting x and z. FrameBlock is used when building an exit;
/// IsFrame may accept additional materials. ResolveTarget returning null disables lighting and travel in that level.
/// Callbacks run inside a call into the owning plugin. Use TryLightPortalAsync from an item or block callback
/// for ignition rules more involved than a single Igniter item.
/// </remarks>
public sealed record PortalProperties
{
    public required Func<IBlock, bool> IsFrame { get; init; }
    public required IBlock FrameBlock { get; init; }
    public required IBlock PortalBlock { get; init; }
    public required Func<ILevel, ILevel?> ResolveTarget { get; init; }
    public Item? Igniter { get; init; }
    public bool IgnitesFromFire { get; init; }
    public int MinWidth { get; init; } = 2;
    public int MaxWidth { get; init; } = 21;
    public int MinHeight { get; init; } = 3;
    public int MaxHeight { get; init; } = 21;
    public int WaitTicks { get; init; } = 80;
    public int CreativeWaitTicks { get; init; } = 1;
    public int EntityWaitTicks { get; init; }

    /// <summary>Source/target dimension coordinate scale by default; override for a portal's own mapping.</summary>
    public Func<ILevel, ILevel, double>? CoordinateScale { get; init; }

    /// <summary>Search radius in blocks; defaults to 128. The Nether instance selects 16 on entry.</summary>
    public Func<ILevel, int>? SearchRadius { get; init; }
}

/// <summary>
/// Process-lifetime portal identity. Unload releases its behaviour, leaving placed blocks inert.
/// Register the identifier again on reload to reactivate them.
/// </summary>
public sealed class PortalType
{
    internal PortalType(string identifier) => this.Identifier = identifier;
    public string Identifier { get; }
    internal int Generation { get; set; }
}
