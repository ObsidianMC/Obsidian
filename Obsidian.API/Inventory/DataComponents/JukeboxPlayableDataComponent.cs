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
        if (!reader.ReadBoolean())
        {
            this.song = new(reader.ReadString());
            return;
        }

        var holder = reader.ReadVarInt();
        this.registryId = holder == 0 ? null : holder - 1;
        if (holder == 0)
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
        writer.WriteBoolean(this.Song.Left is null);
        if (this.Song.Left is string key)
        {
            writer.WriteString(key);
            return;
        }

        writer.WriteVarInt(this.RegistryId is int id ? id + 1 : 0);
        if (this.RegistryId is not null)
            return;

        var value = this.Song.Right!;
        ComponentValueCodecs.WriteSoundHolder(value.SoundEvent, writer);
        writer.WriteChat(value.Description);
        writer.WriteSingle(value.LengthInSeconds);
        writer.WriteVarInt(value.ComparatorOutput);
    }
}
