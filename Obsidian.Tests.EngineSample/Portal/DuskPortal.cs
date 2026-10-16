using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Plugins;
using Obsidian.API.Registries;
using Obsidian.API.World;
using Obsidian.API.World.Generator;

namespace EngineSample.Portal;

/// <summary>
/// Phase 5's portal to the sample's dimension, <c>sample:dusk</c>: a frame of amethyst blocks, lit by using an
/// amethyst shard on it, fills with <c>sample:dusk_portal</c>, which sends what stands in it to the same place in
/// the other level, building a frame there if none is near.
/// </summary>
/// <remarks>
/// <para>
/// The dimension is data, like a data pack's: <c>data/sample/dimension/dusk.json</c> is a flat level of end stone
/// whose type, <c>data/sample/dimension_type/dusk.json</c>, gives it the End's sky and a violet fog on every client.
/// The server creates its level as the plugin loads.
/// </para>
/// <para>
/// The portal block draws with vanilla's Nether portal models (<c>assets/sample/blockstates/dusk_portal.json</c>),
/// so the sample ships no texture for it.
/// </para>
/// </remarks>
public static class DuskPortal
{
    /// <summary>
    /// The sample dimension's identifier, which is its level's name.
    /// </summary>
    public const string Dimension = "sample:dusk";

    /// <summary>
    /// The portal type.
    /// </summary>
    public static PortalType Type { get; private set; } = null!;

    /// <summary>
    /// Registers the portal block and the portal type. Both sides register the block, so that the client knows and
    /// draws it; the client keeps only the portal type's identity.
    /// </summary>
    public static void Register(IPluginRegistry registry)
    {
        // A portal block needs an axis of x or z: the way its frame faces.
        var portal = registry.RegisterBlock("sample:dusk_portal", new BlockProperties
        {
            StateProperties = [StateProperty.Enum("axis", "x", "z")],
            HasCollision = false,
            Opaque = false,
            Hardness = -1,
            LightEmission = 11,
            Item = null
        });

        Type = registry.RegisterPortal("sample:dusk", new PortalProperties
        {
            IsFrame = block => block.Is(BlockIds.AmethystBlock),
            FrameBlock = new SimpleBlockState { Name = "minecraft:amethyst_block" }.ToBlock(),
            PortalBlock = portal.DefaultState,
            Igniter = ItemsRegistry.AmethystShard,
            ResolveTarget = Target
        });
    }

    // From the dimension back to the overworld, its world's root level, and from that to the dimension. Other
    // levels have no dusk portals: they don't light there.
    private static ILevel? Target(ILevel level)
    {
        if (level is IDimension { DimensionName: Dimension } dusk)
            return dusk.ParentWorld;

        return level is IWorld world ? world.GetLevel(Dimension) : null;
    }
}
