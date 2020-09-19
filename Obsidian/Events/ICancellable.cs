namespace Obsidian.Events
{
    public interface ICancellable
    {
        bool Cancel { get; set; }
    }
}
