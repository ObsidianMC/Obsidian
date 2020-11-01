namespace Obsidian.API.Events
{
    public interface ICancellable
    {
        bool Cancel { get; set; }
    }
}
