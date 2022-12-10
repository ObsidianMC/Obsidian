namespace Obsidian.API;
public interface IBlock
{
    public static abstract string UnlocalizedName { get; }

    public static abstract int BaseId { get; }

    public int StateId { get; }
}
