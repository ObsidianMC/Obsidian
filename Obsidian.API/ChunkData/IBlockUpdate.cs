namespace Obsidian.API.ChunkData;

public interface IBlockUpdate
{
    public IBlock? Block { get; set; }

    public ILevel Level { get; }
    public Vector Position { get; set; }

    public int Delay { get; set; }
    public int DelayCounter { get; set; }
}
