namespace Obsidian.API.Registry.Codecs.Dialogs;
public sealed record class DialogAction
{
    public required ChatMessage Label { get; set; }

    public required int Width { get; set; }
}
