using System.ComponentModel.DataAnnotations;

namespace Obsidian.API;
public sealed record class Page : INetworkSerializable<Page>
{
    [MaxLength(1024)]
    public required string RawContent { get; set; }

    [MaxLength(1024)]
    public string? FilteredContent { get; set; }

    public static Page Read(INetStreamReader reader) => new()
    {
        RawContent = reader.ReadString(),
        FilteredContent = reader.ReadOptionalString()
    };

    public static void Write(Page value, INetStreamWriter writer)
    {
        writer.WriteString(value.RawContent);
        writer.WriteOptional(value.FilteredContent);
    }
}
