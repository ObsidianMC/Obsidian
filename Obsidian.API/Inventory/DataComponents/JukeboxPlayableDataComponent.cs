namespace Obsidian.API.Inventory.DataComponents;

public sealed record class JukeboxPlayableDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.JukeboxPlayable;

    public override string Identifier => "minecraft:jukebox_playable";

    private EitherHolder<string, JukeboxSong> song;

    public EitherHolder<string, JukeboxSong> Song
    {
        get => this.song;
        init => this.song = value;
    }

    private int? registryId;

    public int? RegistryId
    {
        get => this.registryId;
        init => this.registryId = value;
    }

    private bool showInTooltip;

    public bool ShowInTooltip
    {
        get => this.showInTooltip;
        init => this.showInTooltip = value;
    }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.song = default;
        this.registryId = null;
        this.registryId = reader.ReadRegistryHolder("minecraft:jukebox_song");
        if (this.registryId is null)
        {
            this.song = new(new JukeboxSong
            {
                SoundEvent = ComponentValueCodecs.ReadSoundHolder(reader),
                Description = reader.ReadChat(),
                LengthInSeconds = reader.ReadSingle(),
                ComparatorOutput = reader.ReadVarInt()
            });
        }
    }

    public override void Write(INetStreamWriter writer)
    {
        var id = this.RegistryId ?? (this.Song.Left is string key
            ? ComponentRegistryNames.Id("minecraft:jukebox_song", key) : (int?)null);
        writer.WriteRegistryHolder("minecraft:jukebox_song", id);
        if (id is not null)
            return;

        var value = this.Song.Right!;
        ComponentValueCodecs.WriteSoundHolder(value.SoundEvent, writer);
        writer.WriteChat(value.Description);
        writer.WriteSingle(value.LengthInSeconds);
        writer.WriteVarInt(value.ComparatorOutput);
    }
}
