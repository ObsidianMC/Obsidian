namespace Obsidian.API.Inventory.DataComponents;
public sealed record class JukeboxPlayableDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.JukeboxPlayable;

    public override string Identifier => "minecraft:jukebox_playable";

    public EitherHolder<string, JukeboxSong> Song { get; set; }

    public int? RegistryId { get; set; }

    public bool ShowInTooltip { get; set; }

    public override void Read(INetStreamReader reader)
    {
        if (!reader.ReadBoolean())
        {
            this.Song = new(reader.ReadString());
            return;
        }

        var holder = reader.ReadVarInt();
        this.RegistryId = holder == 0 ? null : holder - 1;
        if (holder == 0)
        {
            this.Song = new(new JukeboxSong
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
