namespace Obsidian;
public class Tag
{
    public string Name { get; init; }
    public string Type { get; init; }
    public bool Replace { get; init; }
    public int[] Entries { get; init; }
    public int Count => Entries.Length;
}
