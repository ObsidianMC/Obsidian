namespace Obsidian.API.Inventory.DataComponents;
public sealed class JukeboxPlayableDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.JukeboxPlayable;

    public string Identifier => "minecraft:jukebox_playable";

    public EitherHolder<string, JukeboxSong> Song { get; set; }

    public bool ShowInTooltip { get; set; }

    public void Read(INetStreamReader reader)
    {
        var resourceLocation = reader.ReadOptionalString();

        if (!string.IsNullOrEmpty(resourceLocation))
        {
            this.Song = new(resourceLocation);
            return;
        }

        this.Song = new(JukeboxSong.Read(reader));
    }

    public void Write(INetStreamWriter writer)
    {
        var isLeft = this.Song.Left != null;

        writer.WriteOptional(this.Song.Left);

        if (isLeft)
            return;

        JukeboxSong.Write(this.Song.Right, writer);
    }
}
