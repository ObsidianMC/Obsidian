namespace Obsidian.API;

public sealed class ClickComponent
{
    public required ClickAction Action { get; set; }

    public required string Value { get; set; }
}

public interface ClickComponentValue
{

}
