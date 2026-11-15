namespace Obsidian.API;
/// <summary>
/// One state of a block: a combination of its property values.
/// </summary>
public interface IBlockState
{
    /// <summary>
    /// Gets the state's id, unique among every block's states.
    /// </summary>
    public int Id { get; }
}
