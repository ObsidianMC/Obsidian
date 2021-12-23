namespace Obsidian.WorldData;

public abstract class TileEntity
{
    //[NbtIgnore]
    public VectorF Position
    {
        get
        {
            return new VectorF(X, Y, Z);
        }
    }

    //[TagName("id")]
    public abstract string Id { get; }
    //[TagName("x")]
    public int X { get; set; }
    //[TagName("y")]
    public int Y { get; set; }
    //[TagName("z")]
    public int Z { get; set; }
}
