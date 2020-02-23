using System;
using Obsidian.Net;

namespace Obsidian.Util.Debug
{
    public class PacketDebugWrapper : IDisposable
    {
        public MinecraftStream Stream { get; }

        public string Description { get; }
        
        public long StartPosition { get; }
        
        public long EndPosition { get; private set; }
        
        public PacketDebugStream DebugStream
        {
            get
            {
                if (Stream.BaseStream is PacketDebugStream debugStream)
                    return debugStream;

                return null;
            }
        }
        
        public PacketDebugWrapper(MinecraftStream stream, string description)
        {
            Stream = stream;
            
            if (DebugStream == null)
                throw new ArgumentException("Incorrect stream used", nameof(stream));

            Description = description;

            StartPosition = DebugStream.Position;
        }

        public void Dispose()
        {
            EndPosition = DebugStream.Position;
            
            DebugStream.Markers.Add(new PacketDebugMarker(Description, StartPosition, EndPosition));
        }
    }
}