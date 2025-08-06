namespace Obsidian.API.Inventory.DataComponents;
public sealed record class JukeboxPlayableDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.JukeboxPlayable;

    public override string Identifier => "minecraft:jukebox_playable";

    public EitherHolder<string, JukeboxSong> Song { get; set; }

    public bool ShowInTooltip { get; set; }

    public override void Read(INetStreamReader reader)
    {
        var resourceLocation = reader.ReadOptionalString();

        if (!string.IsNullOrEmpty(resourceLocation))
        {
            this.Song = new(resourceLocation);
            return;
        }

        this.Song = new(JukeboxSong.Read(reader));
    }

    public override void Write(INetStreamWriter writer)
    {
        var isLeft = this.Song.Left != null;

        writer.WriteOptional(this.Song.Left);

        if (isLeft)
            return;

        JukeboxSong.Write(this.Song.Right, writer);
    }
}
