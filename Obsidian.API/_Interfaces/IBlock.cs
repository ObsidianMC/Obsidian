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

    public bool IsLiquid => this.Material is API.Material.Water or API.Material.Lava;

    public bool IsAir => this.Material is API.Material.Air or API.Material.CaveAir or API.Material.VoidAir;

    [Obsolete]
    public bool IsTransparent => this.IsLiquid || IsAir;

    public bool Is(Material material) => this.Material == material;
}
