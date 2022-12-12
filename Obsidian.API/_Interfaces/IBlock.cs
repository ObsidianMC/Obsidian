namespace Obsidian.API;
public interface IBlock
{
    public static abstract string UnlocalizedName { get; }

    public static abstract int BaseId { get; }

    public Material Material { get; }

    public int StateId { get; }
}
