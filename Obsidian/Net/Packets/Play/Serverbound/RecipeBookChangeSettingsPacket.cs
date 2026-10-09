namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class RecipeBookChangeSettingsPacket
{
    public int BookType { get; private set; }

    public bool IsOpen { get; private set; }

    public bool IsFiltering { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.BookType = reader.ReadVarInt();
        this.IsOpen = reader.ReadBoolean();
        this.IsFiltering = reader.ReadBoolean();
    }
}
