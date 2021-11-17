namespace Obsidian.API;

public class ClickComponent
{
    public EClickAction Action { get; set; }

    public string Value { get; set; }

    public string Translate { get; set; }

    public ClickComponent(EClickAction action, string value, string translate = "")
    {
        Action = action;
        Value = value;
        Translate = translate;
    }
}
