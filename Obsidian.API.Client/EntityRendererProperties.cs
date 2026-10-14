namespace Obsidian.API.Client;

/// <summary>
/// How the Obsidian client draws a mod's entity type: a model layer and a texture from the mod's assets, and the
/// keyframed animations it plays. Register it with <see cref="IClientRegistry.RegisterEntityRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Parts named <c>head</c> turn to where the entity looks, and, without a <see cref="WalkAnimation"/>, parts whose
/// names end in <c>leg</c> swing as it walks (left and right, and front and back, in opposite phases), like vanilla's
/// mobs; other parts stay as modelled unless an animation moves them.
/// </para>
/// <para>
/// The items the server says the entity holds and wears are drawn on parts named like vanilla's: held items in the
/// hands of <c>right_arm</c> and <c>left_arm</c>, and armour pieces on the parts vanilla's humanoid armour covers
/// (<c>head</c>, <c>body</c>, <c>right_arm</c>, <c>left_arm</c>, <c>right_leg</c> and <c>left_leg</c>), following them.
/// A model without such a part doesn't show what would go on it.
/// </para>
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

    /// <summary>
    /// An animation the entity always plays, from its spawn, for example <c>examplemod:golem/idle</c> for
    /// <c>assets/examplemod/animations/entity/golem/idle.json</c>; null for none. A looping one repeats, and one that
    /// doesn't loop holds its last pose.
    /// </summary>
    public string? IdleAnimation { get; init; }

    /// <summary>
    /// An animation played as the entity walks, advancing with the distance it walks and weighted by its speed like
    /// vanilla's walking animations, instead of swinging its legs; null for none.
    /// </summary>
    public string? WalkAnimation { get; init; }

    /// <summary>
    /// An animation played once each time the entity swings its arm, which servers show as mobs attack; null for
    /// none.
    /// </summary>
    public string? AttackAnimation { get; init; }
}
