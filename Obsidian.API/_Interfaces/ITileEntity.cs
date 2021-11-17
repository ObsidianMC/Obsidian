namespace Obsidian.API;

public interface ITileEntity
{
    public string Id { get; }

    public Vector BlockPosition { get; set; }

    public void ToNbt();

    public void FromNbt();
}
