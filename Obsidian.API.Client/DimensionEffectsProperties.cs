namespace Obsidian.API.Client;

/// <summary>
/// How the client draws a mod's dimension type's sky, clouds and fog beyond what the type's synced data says, like
/// NeoForge's dimension special effects. Register it with <see cref="IClientRegistry.RegisterDimensionEffects"/>.
/// </summary>
/// <remarks>
/// <para>
/// The dimension type's data (<c>data/&lt;ns&gt;/dimension_type/&lt;path&gt;.json</c>, sent to clients) decides most
/// of it, for vanilla clients too: its <c>skybox</c> (<c>overworld</c>, <c>end</c> or <c>none</c>), its
/// <c>timelines</c> (<c>#minecraft:in_overworld</c> for the day and night), and the <c>minecraft:visual/</c>
/// environment attributes of the type and its biomes, like <c>sky_color</c>, <c>fog_color</c>,
/// <c>fog_start_distance</c>, <c>fog_end_distance</c>, <c>cloud_color</c> (clouds show when its alpha isn't 0) and
/// <c>cloud_height</c>. These properties add what that format can't say.
/// </para>
/// <para>
/// Textures are read with the client's resources, after the mods are configured; without its file, the clouds are
/// vanilla's and a sky quad is plain colour.
/// </para>
/// </remarks>
public sealed class DimensionEffectsProperties
{
    /// <summary>
    /// The clouds' texture instead of vanilla's, like <c>examplemod:environment/clouds</c> for
    /// <c>assets/examplemod/textures/environment/clouds.png</c>, read like vanilla's <c>clouds.png</c>: each pixel
    /// that isn't transparent is a 12-block cell of cloud in the pixel's colour, and the texture repeats. Null for
    /// vanilla's clouds.
    /// </summary>
    public string? CloudTexture { get; init; }

    /// <summary>
    /// The textures <see cref="IDimensionEffects.RenderSky"/> may draw with, like <c>examplemod:environment/sun</c>
    /// for <c>assets/examplemod/textures/environment/sun.png</c>.
    /// </summary>
    public IReadOnlyList<string> SkyTextures { get; init; } = [];

    /// <summary>
    /// Code that adjusts the sky, fog and clouds each frame, or draws into the sky.
    /// </summary>
    public IDimensionEffects? Effects { get; init; }
}
