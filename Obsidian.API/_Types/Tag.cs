namespace Obsidian.API;
public sealed record class Tag
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Replace { get; init; }
    public required int[] Entries { get; init; }
    public int Count => Entries.Length;
}
