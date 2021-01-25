using Obsidian.API;

namespace Obsidian
{
    public class Score : IScore
    {
        public string DisplayText { get; set; }
        public int Value { get; set; }
    }
}
