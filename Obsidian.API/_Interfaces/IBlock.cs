namespace Obsidian.API;
public interface IBlock
{
    public string UnlocalizedName { get; }

    public int BaseId { get; }

    public Material Material { get; }

    public int StateId { get; }
}
