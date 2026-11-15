namespace Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
/// <summary>
/// Contains an armor trim pattern's asset identifier, description and decal flag.
/// </summary>
public sealed record class TrimPatternElement : INetworkSerializable<TrimPatternElement>
{
    /// <summary>
    /// Gets the pattern's translated display description.
    /// </summary>
    public required TrimDescription Description { get; init; }

    /// <summary>
    /// Gets the client trim pattern asset identifier, read from <c>asset_id</c>.
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// Gets the pattern's decal rendering flag, read from <c>decal</c>.
    /// </summary>
    public required bool Decal { get; init; }

    /// <summary>
    /// Reads a trim pattern's asset identifier, description and decal flag from the network stream.
    /// </summary>
    public static TrimPatternElement Read(INetStreamReader reader) => new()
    {
        AssetId = reader.ReadString(),
        Description = TrimDescription.Read(reader),
        Decal = reader.ReadBoolean()
    };

    /// <summary>
    /// Writes a trim pattern's asset identifier, description and decal flag to the network stream.
    /// </summary>
    public static void Write(TrimPatternElement value, INetStreamWriter writer)
    {
        writer.WriteString(value.AssetId);

        TrimDescription.Write(value.Description, writer);
        writer.WriteBoolean(value.Decal);
    }
}
