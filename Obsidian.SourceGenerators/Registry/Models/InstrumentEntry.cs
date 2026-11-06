namespace Obsidian.SourceGenerators.Registry.Models;

/// <summary>
/// An instrument from <c>Assets/instruments.json</c>, so instrument tags (<c>instrument/regular_goat_horns</c>) resolve
/// to the network ids the instrument registry is sent with.
/// </summary>
internal sealed class InstrumentEntry(string tag, int registryId) : ITaggable
{
    public string Tag { get; } = tag;

    public string Type => "instrument";

    public string Parent => "instrument";

    public string GetTagValue() => registryId.ToString();
}
