namespace Obsidian.API.Client;

/// <summary>
/// A layer drawn over a mod entity's model (see <see cref="EntityRendererProperties.Layers"/>), like vanilla's render
/// layers: a model layer with a texture, posed like the entity's model.
/// </summary>
/// <remarks>
/// Its parts are posed by name like the entity's model's (heads turn, legs swing, the renderer's animations move
/// them), so a layer whose parts are named like the model's follows it, as vanilla's layers copy their model's pose.
/// </remarks>
public sealed record EntityRenderLayer
{
    /// <summary>
    /// The model layer, named like <see cref="EntityRendererProperties.Model"/>; null for the entity's own model, for
    /// a texture drawn over it, like vanilla's glowing eyes.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// The texture, named like <see cref="EntityRendererProperties.Texture"/>.
    /// </summary>
    public required string Texture { get; init; }

    /// <summary>
    /// How the layer is drawn: <see cref="EntityRenderType.Cutout"/> by default.
    /// </summary>
    public EntityRenderType RenderType { get; init; }

    /// <summary>
    /// The colour the texture is multiplied by, as <c>0xRRGGBB</c>; white by default.
    /// </summary>
    public int Tint { get; init; } = 0xFFFFFF;

    /// <summary>
    /// Chooses the layer's look from the entity's synced metadata, like
    /// <see cref="EntityRendererProperties.TextureProvider"/>: its texture and tint, or null to hide it. Without one,
    /// the layer always shows <see cref="Texture"/> with <see cref="Tint"/>.
    /// </summary>
    /// <remarks>
    /// It runs at most once per metadata change, on the same terms as the renderer's texture provider. If it throws,
    /// the layer shows <see cref="Texture"/> with <see cref="Tint"/>.
    /// </remarks>
    public Func<IReadOnlyDictionary<byte, object?>, EntityLayerAppearance?>? Provider { get; init; }
}

/// <summary>
/// How an <see cref="EntityRenderLayer"/> looks for an entity's synced metadata.
/// </summary>
/// <param name="Texture">The texture, named like <see cref="EntityRendererProperties.Texture"/>.</param>
/// <param name="Tint">The colour the texture is multiplied by, as <c>0xRRGGBB</c>.</param>
public readonly record struct EntityLayerAppearance(string Texture, int Tint = 0xFFFFFF);

/// <summary>
/// How a layer or a part drawn by code is drawn, like vanilla's entity render types.
/// </summary>
public enum EntityRenderType
{
    /// <summary>
    /// Opaque, with transparent pixels cut out, lit and shaded like the entity, and tinted red as it's hurt: vanilla's
    /// <c>entity_cutout</c>.
    /// </summary>
    Cutout,

    /// <summary>
    /// Blended by the texture's alpha, otherwise like <see cref="Cutout"/>: vanilla's <c>entity_translucent</c>.
    /// </summary>
    Translucent,

    /// <summary>
    /// Full-bright, whatever the light, unshaded and never tinted red, blended by the texture's alpha and hiding
    /// nothing behind it: vanilla's <c>eyes</c>, for glowing eyes and markings.
    /// </summary>
    Emissive
}
