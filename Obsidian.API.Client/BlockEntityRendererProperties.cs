namespace Obsidian.API.Client;

/// <summary>
/// How a mod's block entity type is drawn each frame, on top of its block's model, like vanilla's block entity
/// renderers: parts that move without meshing the world again, such as a machine's spinning gear. Register it with
/// <see cref="IClientRegistry.RegisterBlockEntityRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Provider"/> describes the parts from the block entity's synced data, once per update, and the client
/// animates them each frame; <see cref="Renderer"/> is code that draws them itself each frame. Give either, or both.
/// </para>
/// <para>
/// Block entities are drawn within 64 blocks of the camera and in view, like vanilla's, with the light at their block.
/// Parts that change only when the data does (cable arms, facades) belong in the block's model instead (see
/// <see cref="IClientRegistry.RegisterBlockEntityModel"/>), which costs nothing per frame.
/// </para>
/// </remarks>
public sealed class BlockEntityRendererProperties
{
    /// <summary>
    /// The models the parts may name, in the block model format, like <c>examplemod:part/gear</c> for
    /// <c>assets/examplemod/models/part/gear.json</c> (block or item models with elements). The client reads them
    /// with the block models; a part naming another model is an error.
    /// </summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>
    /// The parts a block entity draws, from its latest synced update data (plain NBT values: nested compounds are
    /// dictionaries, lists are lists) and its block state; null or none to draw nothing. A part is shown by returning
    /// it and hidden by leaving it out.
    /// </summary>
    /// <remarks>
    /// It runs at most once per update and block state, on the client's game thread inside a call into the mod: keep
    /// it pure and short, and don't mutate nested values. The client turns each part for the frame's time (see
    /// <see cref="BlockEntityRenderPart.Speed"/>). If it throws, the error is logged and the block entity draws
    /// nothing until its next update.
    /// </remarks>
    public Func<IReadOnlyDictionary<string, object?>, IBlock, IReadOnlyList<BlockEntityRenderPart>?>? Provider
    {
        get;
        init;
    }

    /// <summary>
    /// Code that draws a block entity each frame, for what <see cref="Provider"/>'s parts can't do.
    /// </summary>
    public IBlockEntityRenderer? Renderer { get; init; }
}

/// <summary>
/// A part of a block entity's renderer: a declared model, faces, or both, scaled and turned about
/// <see cref="Pivot"/>, then moved by <see cref="Offset"/>. Positions are in sixteenths of a block, like a block
/// model's elements.
/// </summary>
/// <remarks>
/// Its angle at a frame is <see cref="Angle"/> plus <see cref="Speed"/> times the ticks since <see cref="Since"/>,
/// counting the partial tick, so a spinning part turns smoothly between ticks: a machine that starts working puts the
/// level's game time (<c>ILevel.Time</c>) in its update data, and its provider returns
/// <c>new BlockEntityRenderPart { Model = new("examplemod:part/gear"), Speed = 18, Since = started }</c>.
/// </remarks>
public sealed record BlockEntityRenderPart
{
    /// <summary>
    /// The model drawn, one of <see cref="BlockEntityRendererProperties.Models"/>, turned in quarter turns like a
    /// blockstate file's model before the part's own rotation; null for faces alone.
    /// </summary>
    public BlockModelPart? Model { get; init; }

    /// <summary>
    /// Faces drawn with the model, with any block texture, in the format of model data's faces. Their cull faces and
    /// tint indexes are ignored.
    /// </summary>
    public IReadOnlyList<BlockModelFace> Faces { get; init; } = [];

    /// <summary>
    /// The point the part is scaled and turned about, in sixteenths of a block; the block's centre by default.
    /// </summary>
    public VectorF Pivot { get; init; } = new(8, 8, 8);

    /// <summary>
    /// The direction of the axis the part turns about; up by default.
    /// </summary>
    public VectorF Axis { get; init; } = new(0, 1, 0);

    /// <summary>
    /// The part's angle at <see cref="Since"/>, in degrees: counterclockwise seen from where <see cref="Axis"/>
    /// points, looking back at <see cref="Pivot"/>.
    /// </summary>
    public float Angle { get; init; }

    /// <summary>
    /// How fast the part turns, in degrees per tick (20 ticks a second); 0 for a part that doesn't spin.
    /// </summary>
    public float Speed { get; init; }

    /// <summary>
    /// The game tick from which the part turns at <see cref="Speed"/>, as the server's level counts them
    /// (<c>ILevel.Time</c>), which the client follows.
    /// </summary>
    public long Since { get; init; }

    /// <summary>
    /// How far the part is moved once turned, in sixteenths of a block.
    /// </summary>
    public VectorF Offset { get; init; }

    /// <summary>
    /// The part's size, scaled about <see cref="Pivot"/>.
    /// </summary>
    public float Scale { get; init; } = 1;
}
