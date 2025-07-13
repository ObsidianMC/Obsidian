using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;

//TODO: Expand this class to include more properties as needed
public sealed record class DialogElement : INbtSerializable
{
    public required string Type { get; init; }

    public int ButtonWidth { get; set; } = 200;

    public int Columns { get; set; }

    public string? Dialogs { get; set; }

    public required DialogAction ExitAction { get; set; }

    public ChatMessage? ExternalTitle { get; set; }

    public required ChatMessage Title { get; set; }

    public void Write(INbtWriter writer) => throw new NotImplementedException();
}
