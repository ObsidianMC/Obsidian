namespace Obsidian.API.Client;

/// <summary>
/// What a block entity's <see cref="BlockEntityModelProperties.Provider"/> adds to its block's model: models from
/// resource packs, and faces with any block texture, for textures chosen by data such as a facade's.
/// </summary>
public sealed record BlockModelData
{
    /// <summary>
    /// Models to draw in the block, each one of <see cref="BlockEntityModelProperties.Models"/>.
    /// </summary>
    public IReadOnlyList<BlockModelPart> Parts { get; init; } = [];

    /// <summary>
    /// Faces to draw in the block.
    /// </summary>
    public IReadOnlyList<BlockModelFace> Faces { get; init; } = [];

    /// <summary>
    /// Whether <see cref="Parts"/> and <see cref="Faces"/> replace the block state's model rather than adding to it,
    /// for a block whose look comes entirely from its data, or whose own model a facade covers.
    /// </summary>
    public bool ReplacesBlockModel { get; init; }
}

/// <summary>
/// A model drawn in a block, turned like a blockstate file's model: <paramref name="X"/> then <paramref name="Y"/>
/// degrees clockwise (0, 90, 180 or 270) looking along the axis, its faces' cull faces turning with it; then moved by
/// <see cref="Offset"/>.
/// </summary>
/// <param name="Model">The model, one of <see cref="BlockEntityModelProperties.Models"/>.</param>
/// <param name="X">The rotation about the X axis, in degrees.</param>
/// <param name="Y">The rotation about the Y axis, in degrees.</param>
/// <param name="UvLock">Whether textures keep their world orientation as the model turns.</param>
public readonly record struct BlockModelPart(string Model, int X = 0, int Y = 0, bool UvLock = false)
{
    /// <summary>
    /// How far the model is moved after it's turned, in sixteenths of a block on each axis, from -16 to 16: to draw
    /// one model in each of a drive's slots, say. Its textures move with it. A moved face keeps its cull face only
    /// while it still lies within that side of the block.
    /// </summary>
    /// <remarks>
    /// Drawn by a block entity or item renderer, the model is moved before the renderer's transform.
    /// </remarks>
    public VectorF Offset { get; init; }
}

/// <summary>
/// A face drawn in a block, like a face of a block model's element: the <paramref name="Face"/> side of the box from
/// <paramref name="From"/> to <paramref name="To"/>, in sixteenths of a block, with its texture mapped like a model
/// face without <c>uv</c>.
/// </summary>
/// <param name="From">The box's lowest corner, in sixteenths of a block.</param>
/// <param name="To">The box's highest corner, in sixteenths of a block.</param>
/// <param name="Face">The side of the box drawn.</param>
/// <param name="Texture">A block texture, like <c>minecraft:block/stone</c> or <c>examplemod:block/cable</c>.</param>
public sealed record BlockModelFace(VectorF From, VectorF To, BlockFace Face, string Texture)
{
    /// <summary>
    /// The side of the block whose neighbour hides the face when it's opaque, or null to always draw it.
    /// </summary>
    public BlockFace? CullFace { get; init; }

    /// <summary>
    /// The face's tint index, which the block's tint (see <see cref="IClientRegistry.RegisterBlockTint"/>) colours if
    /// it's 0 or more.
    /// </summary>
    public int TintIndex { get; init; } = -1;

    /// <summary>
    /// Whether the face is shaded by its direction, like a model element's <c>shade</c>.
    /// </summary>
    public bool Shade { get; init; } = true;

    /// <summary>
    /// The light level, from 0 to 15, the face is lit with at least, like a model element's <c>light_emission</c>:
    /// 15 for a face that glows in the dark. It lights the face alone, not the blocks around it.
    /// </summary>
    public int LightEmission { get; init; }
}
