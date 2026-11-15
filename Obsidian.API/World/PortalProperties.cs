using Obsidian.API.Inventory;

namespace Obsidian.API.World;

/// <summary>A rectangular portal, registered during ConfigureRegistry.</summary>
/// <remarks>
/// PortalBlock must have an axis property accepting x and z. Several types may share one, such as vanilla's
/// <c>minecraft:nether_portal</c>: each portal remembers the type that lit or built it, and stays inert while that
/// type isn't registered; a portal block nothing lit, as one placed by a command, belongs to the earliest registered
/// type using it (the Nether for vanilla's block). FrameBlock is used when building an exit;
/// IsFrame may accept additional materials. ResolveTarget returning null disables lighting and travel in that level.
/// Callbacks run inside a call into the owning plugin. Use TryLightPortalAsync from an item or block callback
/// for ignition rules more involved than a single Igniter item.
/// </remarks>
public sealed record PortalProperties
{
    /// <summary>
    /// Gets the predicate accepting block states as portal frame material.
    /// </summary>
    public required Func<IBlock, bool> IsFrame { get; init; }
    /// <summary>
    /// Gets the block state used when building an exit portal's frame.
    /// </summary>
    public required IBlock FrameBlock { get; init; }
    /// <summary>
    /// Gets the portal interior state, whose axis property must accept <c>x</c> and <c>z</c>.
    /// </summary>
    public required IBlock PortalBlock { get; init; }
    /// <summary>
    /// Gets the destination resolver; a <c>null</c> result disables lighting and travel in the source level.
    /// </summary>
    public required Func<ILevel, ILevel?> ResolveTarget { get; init; }
    /// <summary>
    /// Gets the optional item accepted for lighting the frame.
    /// </summary>
    public Item? Igniter { get; init; }
    /// <summary>
    /// Gets whether fire can light this portal type.
    /// </summary>
    public bool IgnitesFromFire { get; init; }
    /// <summary>
    /// Gets the minimum interior width in blocks.
    /// </summary>
    public int MinWidth { get; init; } = 2;
    /// <summary>
    /// Gets the maximum interior width in blocks.
    /// </summary>
    public int MaxWidth { get; init; } = 21;
    /// <summary>
    /// Gets the minimum interior height in blocks.
    /// </summary>
    public int MinHeight { get; init; } = 3;
    /// <summary>
    /// Gets the maximum interior height in blocks.
    /// </summary>
    public int MaxHeight { get; init; } = 21;
    /// <summary>
    /// Gets the portal exposure time in ticks required for a noncreative player to travel.
    /// </summary>
    public int WaitTicks { get; init; } = 80;
    /// <summary>
    /// Gets the portal exposure time in ticks required for a creative player to travel.
    /// </summary>
    public int CreativeWaitTicks { get; init; } = 1;
    /// <summary>
    /// Gets the portal exposure time in ticks required for a nonplayer entity to travel.
    /// </summary>
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
    /// <summary>
    /// Gets the portal type's persistent namespaced identifier.
    /// </summary>
    public string Identifier { get; }
    internal int Generation { get; set; }
}
