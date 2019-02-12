//By David Norgren, feb 2013
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NbsPlayerPlugin 
{
    public class NoteBlock 
    {
        /// <summary>A simple class for storing the note blocks.</summary>
        public short Tick, Layer;
        public byte Instrument, Key;

        public NoteBlock(short tick, short layer, byte inst, byte key)
        {
            Tick = tick;
            Layer = layer;
            Instrument = inst;
            Key = key;
        }
    }
}
