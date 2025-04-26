namespace Obsidian.Nbt.Utilities;
internal sealed class NbtWriterState
{
    public int ListSize { get; init; }

    public int ListIndex { get; set; }

    public NbtTagType? ExpectedListType { get; init; }

    public required NbtWriterState? PreviousState { get; init; }

    public required NbtTagType? ParentTagType { get; init; }

    public List<string> ChildrenAdded { get; init; }

    public bool HasExpectedListType(NbtTagType type) =>
        this.ExpectedListType == type || (this.PreviousState?.HasExpectedListType(type) ?? false);
}
