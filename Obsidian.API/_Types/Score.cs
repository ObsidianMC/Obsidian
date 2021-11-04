namespace Obsidian.API
{
    public sealed class Score
    {
        public Score(string displayText, int value)
        {
            DisplayText = displayText;
            Value = value;
        }

        public string DisplayText { get; internal set; }

        public int Value { get; internal set; }
    }
}
