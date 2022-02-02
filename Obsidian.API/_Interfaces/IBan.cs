namespace Obsidian.API;

public interface IBan
{
    public string Username { get; set; }
    public Guid Uuid { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime TimeStamp { get; set; }
}