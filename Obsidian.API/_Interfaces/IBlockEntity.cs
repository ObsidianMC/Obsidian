namespace Obsidian.API;

/// <summary>
/// A block entity: what the world keeps for one block besides its state. Every block entity the server stores is a
/// <see cref="BlockEntities.BlockEntity"/>.
/// </summary>
public interface IBlockEntity
{
    /// <summary>
    /// The block entity type's identifier, like <c>minecraft:chest</c>.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The position of its block.
    /// </summary>
    public Vector BlockPosition { get; }
}
