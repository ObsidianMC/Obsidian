namespace Obsidian.Util.Debug
{
    public class PacketDebugMarker
    {
        public PacketDebugMarker(string name, long startPosition, long endPosition)
        {
            Name = name;
            StartPosition = startPosition;
            EndPosition = endPosition;
        }

        public string Name { get; }
        public long StartPosition { get; }
        
        public long EndPosition { get; }

        public long Length => EndPosition - StartPosition;
        
        public byte[] Data { get; set; }
    }
}