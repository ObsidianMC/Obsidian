using System.Numerics;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;

namespace Obsidian.API.Client;

/// <summary>
/// Code that draws a mod's block entities each frame, like vanilla's <c>BlockEntityRenderer.render</c>: for
/// animations <see cref="BlockEntityRendererProperties.Provider"/>'s parts can't describe, such as ones following
/// state the block entity keeps on the client. Give it as <see cref="BlockEntityRendererProperties.Renderer"/>.
/// </summary>
public interface IBlockEntityRenderer
{
    /// <summary>
    /// Draws a block entity into <paramref name="buffer"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It runs each frame for each of the type's block entities within 64 blocks of the camera and in view, on the
    /// client's game thread inside a call into the mod. That's the thread <c>BlockEntity.ClientTick</c> runs on, so it
    /// reads what ticks keep in the block entity's fields without locking, and interpolates between the last two ticks
    /// with <paramref name="partialTick"/>. A block entity is drawn from its first client tick on, once its live
    /// instance exists.
    /// </para>
    /// <para>
    /// Keep it short and free of allocations, as the frame waits for it. If it throws, the error is logged, what it
    /// drew that frame is dropped, and it isn't called for the block entity again until its next update.
    /// </para>
    /// </remarks>
    /// <param name="blockEntity">The mod's live client instance; cast it to the mod's class.</param>
    /// <param name="partialTick">How far the frame is between the last client tick and the next, from 0 to 1.</param>
    /// <param name="buffer">What the frame draws the block entity's parts with.</param>
    public void Render(BlockEntity blockEntity, float partialTick, IBlockEntityRenderBuffer buffer);
}

/// <summary>
/// What an <see cref="IBlockEntityRenderer"/> draws into for one block entity in a frame. Transforms are in blocks,
/// from the block's lowest corner, like vanilla's pose stack at the block entity; the light is the block's.
/// </summary>
public interface IBlockEntityRenderBuffer
{
    /// <summary>
    /// Draws a declared model with a transform.
    /// </summary>
    /// <param name="model">
    /// One of <see cref="BlockEntityRendererProperties.Models"/>, turned in quarter turns like a blockstate file's
    /// model before <paramref name="transform"/>.
    /// </param>
    /// <param name="transform">Where the model's block space goes, in blocks from the block's lowest corner.</param>
    /// <exception cref="ArgumentException">
    /// The model isn't one of the renderer's models, or its rotation isn't in quarter turns.
    /// </exception>
    public void Model(BlockModelPart model, Matrix4x4 transform);

    /// <summary>
    /// Draws a face, a quad with a block texture, with a transform. Its cull face and tint index are ignored.
    /// </summary>
    /// <param name="face">The face, in sixteenths of a block like model data's.</param>
    /// <param name="transform">Where the face's block space goes, in blocks from the block's lowest corner.</param>
    public void Face(BlockModelFace face, Matrix4x4 transform);

    /// <summary>
    /// Draws an item stack as vanilla's item renderer does: its item model, with the model's transform for
    /// <paramref name="display"/>, about the transform's origin, like a dropped item (<see
    /// cref="ItemDisplayContext.Ground"/>) or one in an item frame (<see cref="ItemDisplayContext.Fixed"/>). An
    /// item a block entity shows, like a machine's input.
    /// </summary>
    /// <param name="stack">The stack; an empty one draws nothing.</param>
    /// <param name="transform">Where the item's origin goes, in blocks from the block's lowest corner.</param>
    /// <param name="display">The model's display transform: ground or fixed.</param>
    /// <exception cref="ArgumentException"><paramref name="display"/> is neither ground nor fixed.</exception>
    public void Item(ItemStack stack, Matrix4x4 transform, ItemDisplayContext display = ItemDisplayContext.Ground);
}
