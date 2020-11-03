namespace Obsidian.API
{
    public interface ITextComponent
    {
        public ETextAction Action { get; set; }
        public string Value { get; set; }
        public string Translate { get; set; }
    }
}
