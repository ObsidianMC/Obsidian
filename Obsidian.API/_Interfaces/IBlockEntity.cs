namespace Obsidian.API;

public interface IBlockEntity
{
    public string Id { get; }

    public Vector BlockPosition { get; set; }

    public void ToNbt();

    public void FromNbt();
}
