using System.Numerics;

namespace Obsidian.API.Client;

/// <summary>
/// Code that adjusts or draws a mod's dimension type's sky, fog and clouds each frame, like NeoForge's
/// <c>DimensionSpecialEffects</c>, for what the type's data can't say (see <see cref="DimensionEffectsProperties"/>).
/// Give it as <see cref="DimensionEffectsProperties.Effects"/>; implement either method, or both.
/// </summary>
/// <remarks>
/// Both run on the client's game thread inside a call into the mod, once a frame while the player is in a dimension
/// of the type. Keep them short and free of allocations, as the frame waits for them. If one throws, the error is
/// logged and the effects aren't called again until the mods reload: the dimension is drawn from its data alone.
/// </remarks>
public interface IDimensionEffects
{
    /// <summary>
    /// Adjusts the frame's sky, fog and clouds, as the client worked them out from the dimension type's and biomes'
    /// data, the time and the weather. The player's view (under water, blind, in lava) still applies afterwards.
    /// </summary>
    /// <param name="atmosphere">The frame's sky, fog and clouds.</param>
    /// <returns>The frame's sky, fog and clouds; return <paramref name="atmosphere"/> to keep them.</returns>
    public DimensionAtmosphere Adjust(DimensionAtmosphere atmosphere) => atmosphere;

    /// <summary>
    /// Draws into the sky, after the skybox the dimension type's data chooses: give the type
    /// <c>"skybox": "none"</c> to draw the whole sky here.
    /// </summary>
    /// <param name="atmosphere">The frame's sky, fog and clouds, as <see cref="Adjust"/> left them.</param>
    /// <param name="buffer">What the sky is drawn with.</param>
    public void RenderSky(DimensionAtmosphere atmosphere, IDimensionSkyBuffer buffer)
    {
    }
}

/// <summary>
/// A frame's sky, fog and clouds in a dimension, which <see cref="IDimensionEffects.Adjust"/> may change. Colours are
/// red, green and blue (and alpha) from 0 to 1; distances are in blocks from the camera.
/// </summary>
public readonly record struct DimensionAtmosphere
{
    /// <summary>
    /// The time of day the frame is drawn at, in ticks, with the part of a tick since the last one; changing it has no
    /// effect.
    /// </summary>
    public double TimeOfDay { get; init; }

    /// <summary>
    /// Where the camera is in the world; changing it has no effect.
    /// </summary>
    public VectorD Camera { get; init; }

    /// <summary>
    /// The colour of the sky above (vanilla's sky disc, in an <c>overworld</c> skybox).
    /// </summary>
    public Vector3 SkyColor { get; init; }

    /// <summary>
    /// The colour the world fades into with distance, which is also the colour behind the sky.
    /// </summary>
    public Vector3 FogColor { get; init; }

    /// <summary>
    /// How far from the camera the fog starts.
    /// </summary>
    public float FogStart { get; init; }

    /// <summary>
    /// How far from the camera the fog hides the world.
    /// </summary>
    public float FogEnd { get; init; }

    /// <summary>
    /// The clouds' colour, multiplied with their texture's; an alpha of 0 hides them.
    /// </summary>
    public Vector4 CloudColor { get; init; }

    /// <summary>
    /// The height of the clouds' bottom.
    /// </summary>
    public float CloudHeight { get; init; }
}

/// <summary>
/// What an <see cref="IDimensionEffects"/> draws the sky with in a frame. The sky is centred on the camera and doesn't
/// move with it; vanilla's sun and moon are 100 blocks away. The world and the clouds are drawn over it.
/// </summary>
public interface IDimensionSkyBuffer
{
    /// <summary>
    /// Draws a quad in the sky, blended over what's there: its texture's top left corner at <paramref name="a"/>,
    /// bottom left at <paramref name="b"/>, bottom right at <paramref name="c"/> and top right at
    /// <paramref name="d"/>, multiplied by <paramref name="color"/>.
    /// </summary>
    /// <param name="a">The first corner, in blocks from the camera, with Y up.</param>
    /// <param name="b">The second corner.</param>
    /// <param name="c">The third corner.</param>
    /// <param name="d">The fourth corner.</param>
    /// <param name="color">The colour and opacity, from 0 to 1.</param>
    /// <param name="texture">
    /// One of <see cref="DimensionEffectsProperties.SkyTextures"/>, or null for plain colour.
    /// </param>
    /// <param name="additive">Whether the quad adds light to the sky, like vanilla's sun and stars.</param>
    /// <exception cref="ArgumentException">The texture isn't one of the effects' sky textures.</exception>
    public void Quad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector4 color,
        string? texture = null,
        bool additive = false);
}
