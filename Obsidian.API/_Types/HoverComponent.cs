namespace Obsidian.API
{
    public class HoverComponent
    {
        public EHoverAction Action { get; set; }

        public object Contents { get; set; }

        public string Translate { get; set; }

        public HoverComponent(EHoverAction action, object contents, string translate)
        {
            Action = action;
            Contents = contents;
            Translate = translate;
        }
    }
}
