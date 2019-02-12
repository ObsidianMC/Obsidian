using System.Collections.Generic;

namespace NbsPlayerPlugin
{
    public class NbsLayer
    {
        public NbsLayer()
        {
        }

        public NbsLayer(string name, byte volume)
        {
            this.Name = name;
            this.Volume = volume;
        }

        public string Name { get; set; }
        public byte Volume { get; set; }

        public List<NoteBlock> NoteBlocks { get; set; } = new List<NoteBlock>();
    }
}