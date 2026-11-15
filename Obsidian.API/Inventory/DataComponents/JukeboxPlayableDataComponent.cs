namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// The song a jukebox plays the item as (<c>minecraft:jukebox_playable</c>).
/// </summary>
public sealed record class JukeboxPlayableDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.song.Left,
        this.song.Right,
        this.registryId,
        this.showInTooltip
    });

    /// <inheritdoc/>
    public bool Equals(JukeboxPlayableDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.JukeboxPlayable;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:jukebox_playable";

    private EitherHolder<string, JukeboxSong> song;

    /// <summary>
    /// Gets the song, by its identifier or inline.
    /// </summary>
    public EitherHolder<string, JukeboxSong> Song
    {
        get => this.song;
        init => this.song = value;
    }

    private int? registryId;

    /// <summary>
    /// Gets the song's id in the client's jukebox song registry, or null to send <see cref="Song"/>.
    /// </summary>
    public int? RegistryId
    {
        get => this.registryId;
        init => this.registryId = value;
    }

    private bool showInTooltip;

    /// <summary>
    /// Gets whether the tooltip shows the song. Neither sent to clients nor saved.
    /// </summary>
    public bool ShowInTooltip
    {
        get => this.showInTooltip;
        init => this.showInTooltip = value;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
