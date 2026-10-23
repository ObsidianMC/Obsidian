using System.Numerics;
using Obsidian.API.Inventory;

namespace Obsidian.API.Client;

/// <summary>
/// How a mod's special item model type is drawn, like NeoForge's special model renderers: an item model definition's
/// <c>minecraft:special</c> model naming the type draws with <see cref="Renderer"/>. Register it with
/// <see cref="IClientRegistry.RegisterItemRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// In the definition (<c>assets/&lt;ns&gt;/items/&lt;item&gt;.json</c>), the special model names the type and a base
/// model: <c>{"type":"minecraft:special","model":{"type":"examplemod:gem_display"},"base":"examplemod:item/gem"}</c>.
/// The base model's <c>display</c> transforms place what the renderer draws in each display context, and it's what's
/// drawn instead when the renderer fails or its mod is gone.
/// </para>
/// <para>
/// Fields of the special model besides its type are ignored: register a type for each look, or choose by the stack in
/// the renderer.
/// </para>
/// </remarks>
public sealed class ItemRendererProperties
{
    /// <summary>
    /// The models the renderer may draw, in the block model format, like <c>examplemod:item/gem_core</c> for
    /// <c>assets/examplemod/models/item/gem_core.json</c> (block or item models with elements). The client reads them
    /// with the block models; drawing another model is an error.
    /// </summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>
    /// The code that draws the item.
    /// </summary>
    public required IItemRenderer Renderer { get; init; }
}

/// <summary>
/// Code that draws a mod's special item model type, like NeoForge's <c>SpecialModelRenderer</c>. Give it as
/// <see cref="ItemRendererProperties.Renderer"/>.
/// </summary>
public interface IItemRenderer
{
    /// <summary>
    /// Draws a stack into <paramref name="buffer"/>.
    /// </summary>
    /// <remarks>
    /// It runs each frame for each stack drawn with the type, in menus, in hands, in item frames and on the ground, on
    /// the client's game thread inside a call into the mod. Keep it short and free of allocations, as the frame waits
    /// for it, and don't change the stack. If it throws, the error is logged, what it drew is dropped and the base
    /// model is drawn instead, and it isn't called for the stack again until the stack changes.
    /// </remarks>
    /// <param name="stack">The stack drawn, with its components and the mod's own; read it, don't change it.</param>
    /// <param name="context">Where it's drawn and for whom.</param>
    /// <param name="buffer">What the frame draws the item's parts with.</param>
    public void Render(ItemStack stack, ItemModelContext context, IItemRenderBuffer buffer);
}

/// <summary>
/// What an <see cref="IItemRenderer"/> draws into for one stack in a frame. Transforms are in blocks, in the item's
/// model space: the box from (0, 0, 0) to (1, 1, 1) its base model's elements fill at sixteenths, which the base
/// model's display transform for the context then places, like vanilla's special models.
/// </summary>
public interface IItemRenderBuffer
{
    /// <summary>
    /// Draws a declared model with a transform.
    /// </summary>
    /// <param name="model">
    /// One of <see cref="ItemRendererProperties.Models"/>, turned in quarter turns like a blockstate file's model
    /// before <paramref name="transform"/>.
    /// </param>
    /// <param name="transform">Where the model's block space goes, in the item's model space.</param>
    /// <exception cref="ArgumentException">
    /// The model isn't one of the renderer's models, or its rotation isn't in quarter turns.
    /// </exception>
    public void Model(BlockModelPart model, Matrix4x4 transform);

    /// <summary>
    /// Draws a face, a quad with a block texture, with a transform. Its cull face and tint index are ignored.
    /// </summary>
    /// <param name="face">The face, in sixteenths of a block like model data's.</param>
    /// <param name="transform">Where the face's block space goes, in the item's model space.</param>
    public void Face(BlockModelFace face, Matrix4x4 transform);
}
