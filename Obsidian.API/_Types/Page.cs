using System.ComponentModel.DataAnnotations;

namespace Obsidian.API;
/// <summary>A page of a book and quill or written book.</summary>
public sealed record class Page : INetworkSerializable<Page>
{
    /// <summary>Gets the page's text as written, up to 1024 characters.</summary>
    [MaxLength(1024)]
    public required string RawContent { get; init; }

    /// <summary>Gets the page's text with profanity filtered, or null if filtering didn't change it.</summary>
    [MaxLength(1024)]
    public string? FilteredContent { get; init; }

    /// <summary>Reads a page from a network stream.</summary>
    public static Page Read(INetStreamReader reader) => new()
    {
        RawContent = reader.ReadString(),
        FilteredContent = reader.ReadOptionalString()
    };

    /// <summary>Writes a page to a network stream.</summary>
    public static void Write(Page value, INetStreamWriter writer)
    {
        writer.WriteString(value.RawContent);
        writer.WriteOptional(value.FilteredContent);
    }
}
