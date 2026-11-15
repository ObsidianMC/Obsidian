using Obsidian.API.Registries;

namespace Obsidian.API;
/// <summary>
/// A block in one of its states, as stored in a level.
/// </summary>
public interface IBlock
{
    /// <summary>
    /// Gets the block's identifier, like <c>minecraft:stone</c>.
    /// </summary>
    public string UnlocalizedName { get; }

    /// <summary>
    /// Gets the id of the block's first state; its states' ids follow it.
    /// </summary>
    public int BaseId { get; }
    /// <summary>
    /// Gets the id of the block's default state.
    /// </summary>
    public int DefaultId { get; }
    /// <summary>
    /// Gets the block's id in the block registry, the same for all its states (see <see cref="BlockIds"/>).
    /// </summary>
    public int RegistryId { get; }

    /// <summary>
    /// Gets the block's state, or <see langword="null"/> for a block made without one (it is then in its default state).
    /// </summary>
    public IBlockState State { get; }

    /// <summary>
    /// Gets a value indicating whether the block is water or lava.
    /// </summary>
    public bool IsLiquid => this.RegistryId is BlockIds.Water or BlockIds.Lava;

    /// <summary>
    /// Gets a value indicating whether the block is air, cave air or void air.
    /// </summary>
    public bool IsAir => this.RegistryId is BlockIds.Air or BlockIds.CaveAir or BlockIds.VoidAir;

    /// <summary>
    /// Gets a value indicating whether the block is a liquid or air.
    /// </summary>
    [Obsolete]
    public bool IsTransparent => this.IsLiquid || IsAir;

    /// <summary>
    /// Whether the block is the one with the given id, like <see cref="BlockIds.Stone"/>.
    /// </summary>
    public bool Is(int blockId) => this.RegistryId == blockId;
}
