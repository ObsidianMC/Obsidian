namespace Obsidian.API;
public interface IBlock
{
    public string UnlocalizedName { get; }

    public int BaseId { get; }

    public IBlockState State { get; }

    public Material Material { get; }

    public bool IsLiquid => this.Material is Material.Water or Material.Lava;

    [Obsolete]
    public bool IsTransparent => this.IsLiquid || this.Material is Material.Air or Material.CaveAir or Material.VoidAir;
}
