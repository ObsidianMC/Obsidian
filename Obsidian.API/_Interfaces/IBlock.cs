namespace Obsidian.API;
public interface IBlock
{
    public string UnlocalizedName { get; }

    public int BaseId { get; }
    public int DefaultId { get; }

    public IBlockState State { get; }

    public Material Material { get; }

    public bool IsLiquid => this.Material is Material.Water or Material.Lava;

    public bool IsAir => this.Material is Material.Air or Material.CaveAir or Material.VoidAir;

    [Obsolete]
    public bool IsTransparent => this.IsLiquid || IsAir;

    public bool Is(Material material) => this.Material == material;
}
