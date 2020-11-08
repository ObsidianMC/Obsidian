namespace Obsidian.Util.Collection
{
    public class MatchTable
    {
        public short[] Keys { get; }
        public short[] Values { get; }

        public MatchTable(int capacity)
        {
            Keys = new short[capacity];
            Values = new short[capacity];
        }

        public void Register(short key, short value)
        {
            Keys[key] = value;
            Values[value] = key;
        }
    }
}
