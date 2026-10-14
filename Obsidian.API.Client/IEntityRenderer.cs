using System.Numerics;

namespace Obsidian.API.Client;

/// <summary>
/// Code that draws a mod's entities each frame, like vanilla's <c>EntityRenderer.render</c>: for what the model, its
/// animations and layers can't describe. Give it as <see cref="EntityRendererProperties.Renderer"/>.
/// </summary>
public interface IEntityRenderer
{
    /// <summary>
    /// Draws an entity into <paramref name="buffer"/>, after its model and layers.
    /// </summary>
    /// <remarks>
    /// It runs each frame for each of the type's entities in view, on the client's game thread inside a call into the
    /// mod. Keep it short and free of allocations, as the frame waits for it. If it throws, the error is logged, what
    /// it drew that frame is dropped, and it isn't called for the entity again until its metadata changes.
    /// </remarks>
    /// <param name="entity">The entity, as the frame shows it.</param>
    /// <param name="buffer">What the frame draws the entity's parts with.</param>
    public void Render(in EntityRenderContext entity, IEntityRenderBuffer buffer);
}

/// <summary>
/// A mod entity as a frame shows it, for an <see cref="IEntityRenderer"/>: interpolated between its last two ticks.
/// </summary>
public readonly record struct EntityRenderContext
{
    /// <summary>
    /// The entity's id, as <see cref="IClientWorld.GetEntity"/> takes it.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// The client's entity type id.
    /// </summary>
    public EntityType Type { get; init; }

    /// <summary>
    /// The entity's synced metadata, like <see cref="IClientEntity.Metadata"/>. It's the entity's own table,
    /// read-only: don't change values in it.
    /// </summary>
    public IReadOnlyDictionary<byte, object?> Metadata { get; init; }

    /// <summary>
    /// How far the frame is between the last client tick and the next, from 0 to 1.
    /// </summary>
    public float PartialTick { get; init; }

    /// <summary>
    /// The ticks since the client started tracking the entity, counting <see cref="PartialTick"/>.
    /// </summary>
    public float Age { get; init; }

    /// <summary>
    /// Where the entity is, at its feet.
    /// </summary>
    public VectorD Position { get; init; }

    /// <summary>
    /// The yaw its body faces, in degrees, which its model is turned to.
    /// </summary>
    public float BodyYaw { get; init; }

    /// <summary>
    /// The yaw its head faces, in degrees.
    /// </summary>
    public float HeadYaw { get; init; }

    /// <summary>
    /// Its pitch, in degrees: positive looking down.
    /// </summary>
    public float Pitch { get; init; }

    /// <summary>
    /// What the entity's model is drawn with: from model space (16 pixels to a block, Y down, feet at Y 24) to blocks
    /// from <see cref="Position"/>, turned to <see cref="BodyYaw"/>, scaled and tilted as it dies or sleeps. Draw with
    /// it to place parts like the model's, or build a transform of your own.
    /// </summary>
    public Matrix4x4 ModelTransform { get; init; }
}

/// <summary>
/// What an <see cref="IEntityRenderer"/> draws into for one entity in a frame, with the light at the entity.
/// </summary>
public interface IEntityRenderBuffer
{
    /// <summary>
    /// Draws a model layer, or one of its parts with its children, with a texture.
    /// </summary>
    /// <remarks>
    /// Parts are posed by name like the entity's model's (heads turn, legs swing, the renderer's animations move them):
    /// name a part differently to keep it as modelled.
    /// </remarks>
    /// <param name="model">
    /// <see cref="EntityRendererProperties.Model"/>, a layer's model, or one of
    /// <see cref="EntityRendererProperties.Models"/>.
    /// </param>
    /// <param name="part">The name of the part drawn, at any depth; null for the whole model.</param>
    /// <param name="texture">The texture, named like <see cref="EntityRendererProperties.Texture"/>.</param>
    /// <param name="transform">
    /// Where the model's space (16 pixels to a block, Y down), or the part's parent's, goes, in blocks from the
    /// entity's position: <see cref="EntityRenderContext.ModelTransform"/> places it like the entity's model.
    /// </param>
    /// <param name="renderType">How it's drawn.</param>
    /// <param name="tint">The colour the texture is multiplied by, as <c>0xRRGGBB</c>.</param>
    /// <exception cref="ArgumentException">The model isn't the renderer's, or has no such part.</exception>
    public void Model(
        string model,
        string? part,
        string texture,
        Matrix4x4 transform,
        EntityRenderType renderType = EntityRenderType.Cutout,
        int tint = 0xFFFFFF);
}
