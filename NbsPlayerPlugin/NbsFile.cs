namespace NbsPlayerPlugin
{
    public class NbsFile
    {
        public string SongName { get; internal set; }
        public string SongAuthor { get; internal set; }
        public string OriginalSongAuthor { get; internal set; }
        public string Description { get; internal set; }
        public short Tempo { get; internal set; }

        public NbsLayer[] Layers { get; internal set; }
    }
}