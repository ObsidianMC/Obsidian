namespace Obsidian.API.Client;

/// <summary>
/// How the Obsidian client draws a mod's entity type: a model layer and a texture from the mod's assets. Register it
/// with <see cref="IClientRegistry.RegisterEntityRenderer"/>.
/// </summary>
/// <remarks>
/// Parts named <c>head</c> turn to where the entity looks, and parts whose names end in <c>leg</c> swing as it walks
/// (left and right, and front and back, in opposite phases), like vanilla's mobs; other parts stay as modelled.
/// </remarks>
public sealed class EntityRendererProperties
{
    /// <summary>
    /// The model layer, for example <c>examplemod:golem</c> for <c>assets/examplemod/models/entity/golem.json</c>:
    /// the texture's size and a tree of parts with their pivots, rotations and cube faces, in the client's model layer
    /// format (see Obsidian's docs on plugin content).
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// The texture, for example <c>examplemod:entity/golem</c> for
    /// <c>assets/examplemod/textures/entity/golem.png</c>. Entity textures are read from <c>textures/entity/</c>.
    /// </summary>
    public required string Texture { get; init; }

    /// <summary>
    /// How much bigger than its model the entity is drawn.
    /// </summary>
    public float Scale { get; init; } = 1;

    /// <summary>
    /// The radius of the entity's shadow, in blocks, like vanilla renderers' shadow radius; 0 for none.
    /// </summary>
    public float ShadowRadius { get; init; } = 0.5f;
}
