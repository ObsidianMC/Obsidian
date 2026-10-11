using Obsidian.API.Registries;

namespace Obsidian.API;
public interface IBlock
{
    public string UnlocalizedName { get; }

    public int BaseId { get; }
    public int DefaultId { get; }
    public int RegistryId { get; }

    public IBlockState State { get; }

    /// <summary>
    /// The vanilla material naming the block, or null for a block a plugin registered.
    /// </summary>
    public Material? Material { get; }

    public bool IsLiquid => this.RegistryId is BlockIds.Water or BlockIds.Lava;

    public bool IsAir => this.RegistryId is BlockIds.Air or BlockIds.CaveAir or BlockIds.VoidAir;

    [Obsolete]
    public bool IsTransparent => this.IsLiquid || IsAir;

    public bool Is(Material material) => this.Material == material;

    /// <summary>
    /// Whether the block is the one with the given id, like <see cref="BlockIds.Stone"/>.
    /// </summary>
    public bool Is(int blockId) => this.RegistryId == blockId;
}
