namespace Obsidian.API;

public interface IBlockEntity
{
    public string Id { get; }

    public Vector BlockPosition { get; }

    public void ToNbt();

    public void FromNbt();

    public IBlockEntity Clone();
}
