using System.IO;
using System.Collections.Generic;

namespace NbsPlayerPlugin
{
    public static class NbsFileReader
    {
        public static NbsFile ReadNbsFile(string path)
        {
            using (var fileStream = File.OpenRead(path))
            using (var binaryReader = new BinaryReader(fileStream))
            {
                var nbsFile = new NbsFile();
                ReadNbsHeader(nbsFile, binaryReader);
                ReadNbsNoteBlocks(nbsFile, binaryReader);
                return nbsFile;
            }        
        }

        private static void ReadNbsHeader(NbsFile nbsFile, BinaryReader binaryReader)
        {
            var layersCount = binaryReader.ReadInt16();
            nbsFile.Layers = new NbsLayer[layersCount];

            for(int i = 0; i < nbsFile.Layers.Length; i++)
            {
                nbsFile.Layers[i] = new NbsLayer();
            }

            nbsFile.SongName = binaryReader.ReadNbsString();
            nbsFile.SongAuthor = binaryReader.ReadNbsString();
            nbsFile.OriginalSongAuthor = binaryReader.ReadNbsString();
            nbsFile.Description = binaryReader.ReadNbsString();
            nbsFile.Tempo = binaryReader.ReadInt16();
            _ = binaryReader.ReadByte(); //Refer to original NbsReader for fields/properties
            _ = binaryReader.ReadByte();
            _ = binaryReader.ReadByte();
            _ = binaryReader.ReadInt32();
            _ = binaryReader.ReadInt32();
            _ = binaryReader.ReadInt32();
            _ = binaryReader.ReadInt32();
            _ = binaryReader.ReadInt32();
            _ = binaryReader.ReadNbsString(); 
        }

        private static void ReadNbsNoteBlocks(NbsFile nbsFile, BinaryReader binaryReader)
        {
            List<NoteBlock> noteBlocks = new List<NoteBlock>();
            //int[] instrumentcount = new int[5];
            //int[] layercount = new int[nbsFile.Layers.Length];

            short tick = -1;
            short jumps = 0;
            while (true)
            {
                jumps = binaryReader.ReadInt16();
                if (jumps == 0)
                {
                    break;
                }
                tick += jumps;
                short layer = -1;
                while (true)
                {
                    jumps = binaryReader.ReadInt16();
                    if (jumps == 0)
                    {
                        break;
                    }
                    layer += jumps;

                    byte instrument = binaryReader.ReadByte();
                    byte key = binaryReader.ReadByte();

                    var noteBlock = new NoteBlock(tick, layer, instrument, key);
                    noteBlocks.Add(noteBlock);
                    
                    //instrumentcount[instrument]++;
                    //
                    //if (layer < layers)
                    //{
                    //    layercount[layer]++;
                    //}
                }
            }

            foreach (NoteBlock noteBlock in noteBlocks)
            {
                var layer = noteBlock.Layer;
                nbsFile.Layers[layer].NoteBlocks.Add(noteBlock);
            }
            //the list is not used anymore
            noteBlocks.Clear();
        }

        /// <summary>Reads a string from the given stream.
        /// Strings in the nbs file consist of a 32 bit integer, followed by that many ASCII bytes.</summary>
        public static string ReadNbsString(this BinaryReader binaryReader)
        {
            int length = binaryReader.ReadInt32();
            string value = "";
            for (int i = 0; i < length; i++)
            {
                value += (char)binaryReader.ReadByte();
            }
            return value;
        }
    }
}